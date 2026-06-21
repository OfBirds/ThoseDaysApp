using Api.Authorization;
using Api.Data;
using Api.Services;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Rolling, structured (compact JSON) log file — one file per day, Information and up.
// Also ships to Seq when SEQ_URL is set (deploy stacks); every event is tagged
// with the app version so logs are filterable per release.
var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL");
var appVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";

var logConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ThoseDays")
    .Enrich.WithProperty("Version", appVersion)
    .WriteTo.Console()
    .WriteTo.File(
        formatter: new CompactJsonFormatter(),
        path: "logs/log-.json",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31);

if (!string.IsNullOrWhiteSpace(seqUrl))
    logConfig = logConfig.WriteTo.Seq(seqUrl);

Log.Logger = logConfig.CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

// Traces → Seq via OTLP. Endpoint is either explicit (OTEL_EXPORTER_OTLP_ENDPOINT)
// or derived from SEQ_URL (Seq listens for OTLP on port 5341).
var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
if (string.IsNullOrWhiteSpace(otlpEndpoint) && !string.IsNullOrWhiteSpace(seqUrl))
{
    var seqUri = new Uri(seqUrl);
    // Full signal path: the exporter uses a programmatic Endpoint as-is and
    // does not append /v1/traces (it only does that for the env-var form).
    otlpEndpoint = $"{seqUri.Scheme}://{seqUri.Host}:5341/ingest/otlp/v1/traces";
}

if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("ThoseDays", serviceVersion: appVersion))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                o.Protocol = OtlpExportProtocol.HttpProtobuf;
            }));
}

builder.Services.Configure<Api.Config.RecalcConfig>(builder.Configuration.GetSection("Recalc"));

builder.Services.AddOpenApi();
builder.Services.AddControllers(o => o.Filters.Add<ResourceOwnershipFilter>());

// IHttpClientFactory — used by ConfigController (IdP liveness/logo) and OidcUserProvisioner
// (userinfo lookup).
builder.Services.AddHttpClient();

// Signed-JWT auth for the local (break-glass) login. The signing key follows the flat-env
// convention (JWT_SIGNING_KEY); when unset we mint an ephemeral random key so local dev needs
// no setup — tokens just don't survive a restart, and a warning fires. A real key MUST be set
// in deploy stacks.
var jwtKeyRaw = builder.Configuration["JWT_SIGNING_KEY"];
byte[] jwtKeyBytes;
if (string.IsNullOrWhiteSpace(jwtKeyRaw))
{
    jwtKeyBytes = RandomNumberGenerator.GetBytes(32);
    Log.Warning("JWT_SIGNING_KEY not set — using an ephemeral signing key. Tokens are " +
        "invalidated on restart; set JWT_SIGNING_KEY (>=32 chars) before deploying.");
}
else
{
    jwtKeyBytes = Encoding.UTF8.GetBytes(jwtKeyRaw);
    if (jwtKeyBytes.Length < 32)
        throw new InvalidOperationException(
            "JWT_SIGNING_KEY must be at least 32 bytes (256 bits) for HMAC-SHA256.");
}
var jwtSigningKey = new SymmetricSecurityKey(jwtKeyBytes);
var jwtExpiryDays = int.TryParse(builder.Configuration["JWT_EXPIRY_DAYS"], out var ed) && ed > 0 ? ed : 30;
builder.Services.AddSingleton<ITokenService>(
    new JwtTokenService(jwtSigningKey, TimeSpan.FromDays(jwtExpiryDays)));

// Dual-scheme auth for the CrimsonRaven migration (docs/auth-crimsonraven.md):
//   "ThoseDays"    — the self-issued HMAC JWT (sub = ThoseDays User.Id); the backup path.
//   "CrimsonRaven" — OIDC access tokens validated against the IdP's JWKS (OIDC_AUTHORITY),
//                    mapped onto a ThoseDays User by OidcUserProvisioner.
// The default "smart" policy scheme peeks the bearer's `iss` and forwards to whichever
// validator matches, so one Authorization header works for both. OIDC is opt-in: when
// OIDC_AUTHORITY is unset (e.g. a stack without an IdP yet) only the local scheme runs.
const string LocalScheme = "ThoseDays";
const string OidcScheme = "CrimsonRaven";

var oidcAuthority = builder.Configuration["OIDC_AUTHORITY"];   // e.g. https://raven-staging.bearsoft.duckdns.org
var oidcAudience = builder.Configuration["OIDC_AUDIENCE"];     // expected `aud` in the access token
var oidcEnabled = !string.IsNullOrWhiteSpace(oidcAuthority);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "smart";
    options.DefaultChallengeScheme = "smart";
});

authBuilder.AddPolicyScheme("smart", "ThoseDays or CrimsonRaven (by token issuer)", options =>
{
    options.ForwardDefaultSelector = ctx =>
    {
        if (oidcEnabled)
        {
            string authHeader = ctx.Request.Headers.Authorization!;
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var token = new JsonWebTokenHandler().ReadJsonWebToken(authHeader["Bearer ".Length..].Trim());
                    if (string.Equals(token.Issuer, oidcAuthority, StringComparison.OrdinalIgnoreCase))
                        return OidcScheme;
                }
                catch { /* unreadable token → let the local validator reject it */ }
            }
        }
        return LocalScheme;
    };
});

authBuilder.AddJwtBearer(LocalScheme, options =>
{
    options.MapInboundClaims = false; // keep `sub` as `sub`, don't remap to NameIdentifier
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = JwtTokenService.Issuer,
        ValidateAudience = true,
        ValidAudience = JwtTokenService.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = jwtSigningKey,
        ValidateLifetime = true,
        NameClaimType = JwtRegisteredClaimNames.Sub,
    };
});

if (oidcEnabled)
{
    authBuilder.AddJwtBearer(OidcScheme, options =>
    {
        options.Authority = oidcAuthority;
        // homelab :9100 is http; the staging/prod instances are https.
        options.RequireHttpsMetadata = oidcAuthority!.StartsWith("https", StringComparison.OrdinalIgnoreCase);
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = oidcAuthority,
            ValidateAudience = !string.IsNullOrWhiteSpace(oidcAudience),
            ValidAudience = oidcAudience,
            ValidateLifetime = true,
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };
    });

    // Maps a CrimsonRaven identity onto a ThoseDays User (link-by-verified-email) and rewrites
    // `sub` to the ThoseDays User.Id so ResourceOwnershipFilter + routes stay unchanged.
    // Needs HttpContext (for the bearer) to resolve email from the IdP userinfo endpoint.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IClaimsTransformation, OidcUserProvisioner>();
}

// Locked-down by default: every endpoint requires auth unless it opts out with
// [AllowAnonymous] (auth, version, unsubscribe, config) or .AllowAnonymous() (SPA fallback).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var connectionString = $"Host={builder.Configuration["DB_HOST"] ?? "localhost"};" +
    $"Port={builder.Configuration["DB_PORT"] ?? "5432"};" +
    $"Database={builder.Configuration["DB_NAME"] ?? "thosedays"};" +
    $"Username={builder.Configuration["DB_USER"] ?? "postgres"};" +
    $"Password={builder.Configuration["DB_PASSWORD"] ?? "postgres"}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<ICycleService, CycleService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// SMTP from flat SMTP_* env keys (same style as DB_*); password stays in the env file.
builder.Services.Configure<Api.Config.SmtpOptions>(o =>
{
    o.Host = builder.Configuration["SMTP_HOST"] ?? "";
    o.Port = int.TryParse(builder.Configuration["SMTP_PORT"], out var p) ? p : 465;
    o.User = builder.Configuration["SMTP_USER"] ?? "";
    o.Password = builder.Configuration["SMTP_PASS"] ?? "";
    o.From = builder.Configuration["SMTP_FROM"] ?? builder.Configuration["SMTP_USER"] ?? "";
    o.AcceptAllCerts = string.Equals(builder.Configuration["SMTP_ACCEPT_ALL_CERTS"], "true",
        StringComparison.OrdinalIgnoreCase);
});
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService<ReleaseNotifier>();
builder.Services.AddHostedService<ReminderNotifier>();
builder.Services.AddHostedService<BackupService>();

var app = builder.Build();

// Apply any pending EF Core migrations on startup. CI applies them as an
// explicit deploy step too; this is an idempotent safety net (no-op if already
// applied).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Serve the bundled SPA (built frontend copied into wwwroot) and fall back to
// index.html for client-side routes. API controllers are matched first; the
// fallback only handles non-/api, non-file requests.
app.UseStaticFiles(new StaticFileOptions
{
    // The service worker is a stable URL with mutable content — never let it be stale-cached (by
    // browsers or any proxy), or installed PWAs freeze on an old build. Always revalidate.
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.Equals("sw.js", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    }
});
app.UseAuthentication();
// A held (unverified-email) OIDC login is authenticated but unmapped — block it with a clear 403
// before it can reach any endpoint. No-op unless OidcUserProvisioner stamped the hold marker.
app.UseMiddleware<EmailVerificationHoldMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html").AllowAnonymous();

try
{
    Log.Information("Starting ThoseDaysApp API");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
