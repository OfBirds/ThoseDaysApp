using Api.Data;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Rolling, structured (compact JSON) log file — one file per day, Information and up.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        formatter: new CompactJsonFormatter(),
        path: "logs/log-.json",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

builder.Services.Configure<Api.Config.RecalcConfig>(builder.Configuration.GetSection("Recalc"));

builder.Services.AddOpenApi();
builder.Services.AddControllers();

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
    app.MapOpenApi();
}

// Serve the bundled SPA (built frontend copied into wwwroot) and fall back to
// index.html for client-side routes. API controllers are matched first; the
// fallback only handles non-/api, non-file requests.
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

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
