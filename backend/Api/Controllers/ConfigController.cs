using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
///   (<c>oidcOnline</c>) and the IdP's current logo URLs (scraped from CR's own login page so
///   the logo stays single-sourced at the IdP).</item>
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
    private static readonly TimeSpan LogoTtl = TimeSpan.FromMinutes(10);
    private static DateTime _checkedUtc = DateTime.MinValue, _logoCheckedUtc = DateTime.MinValue;
    private static bool _online;
    private static string? _logoUrl, _logoUrlDark;

    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken ct)
    {
        var authority = config["OIDC_AUTHORITY"];
        var enabled = !string.IsNullOrWhiteSpace(authority);
        var online = enabled && await IsOnlineAsync(authority!, ct);
        if (online) await ResolveLogosAsync(authority!, ct);

        // Start from the recalc constants (camelCase, same shape the SPA already consumes)
        // and merge the OIDC fields onto the same object.
        var payload = JsonSerializer.SerializeToNode(recalc.Value, WebJson)!.AsObject();
        payload["oidcEnabled"] = enabled;
        payload["oidcOnline"] = online;
        payload["oidcAuthority"] = authority;
        payload["oidcClientId"] = config["OIDC_CLIENT_ID"];
        payload["oidcLogoUrl"] = _logoUrl;
        payload["oidcLogoUrlDark"] = _logoUrlDark;
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

    /// <summary>Scrape the IdP's current logo URLs from its public login page so the app can show
    /// the same logo without copying it. The URL carries a per-upload id, so we re-read it
    /// periodically (<see cref="LogoTtl"/>) to pick up logo changes.</summary>
    private async Task ResolveLogosAsync(string authority, CancellationToken ct)
    {
        if (DateTime.UtcNow - _logoCheckedUtc < LogoTtl && _logoUrl is not null) return;
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var html = await client.GetStringAsync($"{authority.TrimEnd('/')}/ui/v2/login/loginname", ct);
            // logo-<id> (light) and logo-dark-<id> — the page links both.
            _logoUrl = Match(html, @"https?://[^""'\\]+/policy/label/logo-\d+");
            _logoUrlDark = Match(html, @"https?://[^""'\\]+/policy/label/logo-dark-\d+");
        }
        catch { /* keep last known on failure */ }
        finally { _logoCheckedUtc = DateTime.UtcNow; }
    }

    private static string? Match(string s, string pattern)
    {
        var m = Regex.Match(s, pattern);
        return m.Success ? m.Value : null;
    }
}
