using Api.Config;
using Api.Data;
using Api.Models;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

/// <summary>Tests for the periodic backup writer.</summary>
public class BackupServiceTests
{
    [Theory]
    [InlineData("daily", 1)]
    [InlineData("weekly", 7)]
    [InlineData("monthly", 30)]
    [InlineData(null, 30)]
    [InlineData("nonsense", 30)]
    public void ParseInterval_MapsKnownValues(string? value, int expectedDays)
    {
        Assert.Equal(TimeSpan.FromDays(expectedDays), BackupService.ParseInterval(value));
    }

    private static (BackupService svc, string dir, Guid userId) Create(
        bool seedCycles = true, int? cycleCount = null)
    {
        var dbName = $"backup_{Guid.NewGuid()}";
        var dir = Path.Combine(Path.GetTempPath(), $"tdbackup_{Guid.NewGuid():N}");

        var seedOpts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        var userId = Guid.NewGuid();
        using (var db = new AppDbContext(seedOpts))
        {
            db.Users.Add(new User { Id = userId, Email = "e@x.com", PasswordHash = "h" });
            if (seedCycles)
                foreach (var d in new[] { "2026-01-01", "2026-01-29", "2026-02-26" })
                    db.Cycles.Add(new Cycle
                    {
                        UserId = userId,
                        StartDate = DateTime.SpecifyKind(DateTime.Parse(d), DateTimeKind.Utc),
                        DurationDays = 5,
                    });
            db.SaveChanges();
        }

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<ICycleService, CycleService>();
        services.Configure<RecalcConfig>(_ => { });
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new RecalcConfig
        {
            Weights = [3, 2, 1],
            DefaultCycleLength = 28,
            DefaultPeriodDuration = 5,
            ForecastCount = 15,
        }));
        var sp = services.BuildServiceProvider();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BACKUP_DIR"] = dir,
            ["BACKUP_CYCLE_COUNT"] = cycleCount?.ToString(),
        }).Build();

        var svc = new BackupService(sp.GetRequiredService<IServiceScopeFactory>(), config,
            NullLogger<BackupService>.Instance);
        return (svc, dir, userId);
    }

    [Fact]
    public async Task RunBackup_WritesFilePerUser_WithConventionName()
    {
        var (svc, dir, userId) = Create();

        var written = await svc.RunBackupAsync(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        var file = Assert.Single(written);
        var name = Path.GetFileName(file);
        var userShort = userId.ToString("N")[..8];
        Assert.StartsWith($"thosedays-backup-{userShort}-full-", name);
        Assert.True(File.Exists(file));
        Assert.Contains("\"kind\": \"backup\"", await File.ReadAllTextAsync(file));

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task RunBackup_NoCycles_WritesNothing()
    {
        var (svc, dir, _) = Create(seedCycles: false);

        var written = await svc.RunBackupAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Empty(written);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task RunBackup_CycleCountOverride_ProducesPatchScope()
    {
        var (svc, dir, _) = Create(cycleCount: 2);

        var written = await svc.RunBackupAsync(DateTime.UtcNow, CancellationToken.None);

        var name = Path.GetFileName(Assert.Single(written));
        Assert.Contains("-patch-", name);

        Directory.Delete(dir, recursive: true);
    }
}
