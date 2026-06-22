using System.Text.Json;
using Api.Config;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Api.Controllers;

/// <summary>
/// Public, non-secret runtime config for the SPA. Two concerns share this one endpoint:
/// <list type="bullet">
///   <item>the tunable recalculation constants (<see cref="RecalcConfig"/>), consumed by the
///   calendar + stats views; and</item>
///   <item>the CrimsonRaven (OIDC) front-door config — the single Docker image is promoted
///   across stacks, so the authority/client (which differ per stack) can't be baked at build
///   time; the SPA fetches them here at startup. Also reports whether CrimsonRaven is reachable
///   (<c>oidcOnline</c>). Branding (logo, theme) is owned by CrimsonRaven/Keycloak's own login
///   pages, so the app no longer scrapes a logo from the IdP.</item>
/// </list>
/// Both sets of fields are returned flat at the top level; the SPA reads each independently.
/// Only public values — no secrets.
/// </summary>
[ApiController]
[Route("api/config")]
[AllowAnonymous]
public class ConfigController(
    IOptions<RecalcConfig> recalc,
    IConfiguration config,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan OnlineTtl = TimeSpan.FromSeconds(60);
    private static DateTime _checkedUtc = DateTime.MinValue;
    private static bool _online;

    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken ct)
    {
        var authority = config["OIDC_AUTHORITY"];
        var enabled = !string.IsNullOrWhiteSpace(authority);
        var online = enabled && await IsOnlineAsync(authority!, ct);

        // Start from the recalc constants (camelCase, same shape the SPA already consumes)
        // and merge the OIDC fields onto the same object.
        var payload = JsonSerializer.SerializeToNode(recalc.Value, WebJson)!.AsObject();
        payload["oidcEnabled"] = enabled;
        payload["oidcOnline"] = online;
        payload["oidcAuthority"] = authority;
        payload["oidcClientId"] = config["OIDC_CLIENT_ID"];
        // Login mode: 'crimsonraven' (default) → CR only; 'legacy' → the app's email/password form only.
        // A manual env break-glass (AUTH_MODE=legacy) for CR maintenance — never both at once.
        payload["authMode"] = string.Equals(config["AUTH_MODE"], "legacy", StringComparison.OrdinalIgnoreCase)
            ? "legacy" : "crimsonraven";
        return Ok((object)payload);
    }

    /// <summary>Is CrimsonRaven's OIDC metadata reachable? Cached for <see cref="OnlineTtl"/>.</summary>
    private async Task<bool> IsOnlineAsync(string authority, CancellationToken ct)
    {
        if (DateTime.UtcNow - _checkedUtc < OnlineTtl) return _online;
        await Gate.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _checkedUtc < OnlineTtl) return _online;
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(2.5);
            using var resp = await client.GetAsync(
                $"{authority.TrimEnd('/')}/.well-known/openid-configuration", ct);
            _online = resp.IsSuccessStatusCode;
        }
        catch { _online = false; }
        finally { _checkedUtc = DateTime.UtcNow; Gate.Release(); }
        return _online;
    }
}
