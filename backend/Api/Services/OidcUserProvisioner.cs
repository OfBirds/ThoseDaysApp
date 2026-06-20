using System.Security.Claims;
using System.Text.Json;
using Api.Data;
using Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Api.Services;

/// <summary>
/// Maps a CrimsonRaven (OIDC) identity onto a ThoseDays <see cref="User"/> so the rest of the
/// app keeps comparing route ids to a ThoseDays <c>User.Id</c> GUID, unchanged. For a
/// CrimsonRaven-issued principal it:
/// <list type="number">
///   <item>finds the user by <see cref="User.ExternalSubject"/> (the IdP <c>sub</c>);</item>
///   <item>else links by <b>verified</b> email to an existing row (preserves that user's
///   cycles/predictions/prefs — the ThoseDays <c>User.Id</c> never changes);</item>
///   <item>else creates a new password-less user.</item>
/// </list>
/// It then rewrites the principal's <c>sub</c> to the ThoseDays <c>User.Id</c>. Locally-issued
/// tokens (GUID <c>sub</c>) are skipped, which also makes this idempotent.
/// <para>
/// Zitadel's JWT access tokens are minimal and don't carry <c>email</c>/<c>email_verified</c>,
/// so on first login (no <see cref="User.ExternalSubject"/> match yet) we resolve them from
/// the IdP <c>userinfo</c> endpoint using the caller's bearer token. Returning users hit the
/// fast <c>ExternalSubject</c> path and make no extra call.
/// </para>
/// </summary>
public class OidcUserProvisioner(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration config) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            return principal;

        var sub = identity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        // A GUID sub means a locally-issued token or an already-mapped principal — nothing to do.
        // (Zitadel subjects are numeric snowflake ids, never GUIDs.)
        if (string.IsNullOrEmpty(sub) || Guid.TryParse(sub, out _))
            return principal;

        var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalSubject == sub);
        if (user is null)
        {
            // First login for this subject: get identity details. Prefer claims (cheap),
            // fall back to userinfo since Zitadel's access token omits email.
            var (email, emailVerified) = ReadEmailClaims(identity);
            if (string.IsNullOrWhiteSpace(email))
                (email, emailVerified) = await FetchUserinfoAsync(sub);

            // Link by email to an existing row (preserves that user's data). By default this
            // requires a VERIFIED email so a CrimsonRaven account can't claim another person's
            // ThoseDays data by asserting an unverified address. OIDC_LINK_ALLOW_UNVERIFIED_EMAIL=true
            // relaxes that for stacks whose IdP doesn't verify emails yet (TEMPORARY — re-tighten
            // once email verification is in place, or duplicate accounts get created on cutover).
            var allowUnverified = string.Equals(
                config["OIDC_LINK_ALLOW_UNVERIFIED_EMAIL"], "true", StringComparison.OrdinalIgnoreCase);
            if ((emailVerified || allowUnverified) && !string.IsNullOrWhiteSpace(email))
            {
                var lowered = email.ToLowerInvariant();
                user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == lowered);
            }

            if (user is not null)
            {
                user.ExternalSubject = sub; // link existing row → data preserved
            }
            else
            {
                user = new User { Email = email ?? sub, ExternalSubject = sub, PasswordHash = null };
                db.Users.Add(user);
            }
            await db.SaveChangesAsync();
        }

        // Swap the IdP subject for the ThoseDays User.Id so ResourceOwnershipFilter and every
        // api/user/{userId} route work without change.
        foreach (var c in identity.FindAll(JwtRegisteredClaimNames.Sub).ToList())
            identity.RemoveClaim(c);
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()));
        return principal;
    }

    private static (string? email, bool verified) ReadEmailClaims(ClaimsIdentity identity)
    {
        var email = identity.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? identity.FindFirst(ClaimTypes.Email)?.Value;
        var verified = string.Equals(
            identity.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        return (email, verified);
    }

    /// <summary>Resolve email + email_verified from the IdP userinfo endpoint using the
    /// caller's bearer token (the access token alone doesn't carry them).</summary>
    private async Task<(string? email, bool verified)> FetchUserinfoAsync(string sub)
    {
        var authority = config["OIDC_AUTHORITY"];
        var raw = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(raw)
            || !raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return (null, false);

        try
        {
            var client = httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{authority.TrimEnd('/')}/oidc/v1/userinfo");
            req.Headers.TryAddWithoutValidation("Authorization", raw);
            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return (null, false);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            // sanity: userinfo sub should match the token sub
            if (root.TryGetProperty("sub", out var s) && s.GetString() != sub) return (null, false);
            var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
            var verified = root.TryGetProperty("email_verified", out var v)
                && (v.ValueKind == JsonValueKind.True
                    || (v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
            return (email, verified);
        }
        catch
        {
            return (null, false); // userinfo unreachable → treat as unlinkable (new user)
        }
    }
}
