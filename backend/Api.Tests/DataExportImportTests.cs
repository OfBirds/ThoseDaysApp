using Api.Config;
using Api.Data;
using Api.DTOs;
using Api.Models;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>Export shape, patch semantics, and the file-naming convention.</summary>
public class DataExportImportTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CycleService _svc;
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly RecalcConfig Cfg = new()
    {
        Weights = [3, 2, 1],
        DefaultCycleLength = 28,
        DefaultPeriodDuration = 5,
        ForecastCount = 15,
    };

    public DataExportImportTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"data_{Guid.NewGuid()}").Options;
        _db = new AppDbContext(options);
        _svc = new CycleService(_db, Options.Create(Cfg));
        _db.Users.Add(new User { Id = _userId, Email = "e@x.com", PasswordHash = "secret-hash", NotifyReleases = true });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private void SeedCycles(params string[] startDates)
    {
        foreach (var d in startDates)
            _db.Cycles.Add(new Cycle
            {
                UserId = _userId,
                StartDate = DateTime.SpecifyKind(DateTime.Parse(d), DateTimeKind.Utc),
                DurationDays = 5,
            });
        _db.SaveChanges();
    }

    // --- Export ---------------------------------------------------------------

    [Fact]
    public async Task Export_Full_IncludesAllCycles_NoSecrets()
    {
        SeedCycles("2026-01-01", "2026-01-29", "2026-02-26");

        var doc = await _svc.BuildExportAsync(_userId, cycles: null, kind: "export", appVersion: "1.2.3");

        Assert.Equal(1, doc.SchemaVersion);
        Assert.Equal("full", doc.Scope);
        Assert.Equal(3, doc.CycleCount);
        Assert.Equal("2026-01-01", doc.Range!.Start);
        Assert.Equal("2026-03-02", doc.Range.End); // last start + 5-1 days
        // Account metadata present but never secrets.
        Assert.Equal("e@x.com", doc.Account!.Email);
        var serialized = System.Text.Json.JsonSerializer.Serialize(doc);
        Assert.DoesNotContain("secret-hash", serialized);
        Assert.DoesNotContain("nsubscribe", serialized); // no UnsubscribeToken leaked
    }

    [Fact]
    public async Task Export_Patch_ReturnsMostRecentN_WithRange()
    {
        SeedCycles("2026-01-01", "2026-01-29", "2026-02-26", "2026-03-26");

        var doc = await _svc.BuildExportAsync(_userId, cycles: 2, kind: "export", appVersion: null);

        Assert.Equal("patch", doc.Scope);
        Assert.Equal(2, doc.CycleCount);
        Assert.Equal("2026-02-26", doc.Range!.Start); // the last two
        Assert.Equal("2026-03-30", doc.Range.End);
    }

    // --- Patch import ---------------------------------------------------------

    [Fact]
    public async Task Patch_ReplacesOnlyInWindow_KeepsNeighbours_RegeneratesForecast()
    {
        // Existing: Jan 1, Feb 15 (inside the import window), May 1, Jun 1.
        SeedCycles("2026-01-01", "2026-02-15", "2026-05-01", "2026-06-01");

        // Import a window covering [Feb 10, Mar 15] — replaces the Feb 15 cycle,
        // adds Mar; keeps Jan/May/Jun (outside the window).
        var imported = new List<ExportCycle>
        {
            new() { StartDate = "2026-02-10", DurationDays = 4 },
            new() { StartDate = "2026-03-10", DurationDays = 6 },
        };

        var result = await _svc.PatchCyclesAsync(_userId, imported);

        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Removed); // the Feb 15 cycle fell in [Feb10, Mar15]

        var cycles = await _svc.GetUserCyclesAsync(_userId);
        var starts = cycles.Select(c => c.StartDate.ToString("yyyy-MM-dd")).ToList();
        Assert.Contains("2026-01-01", starts); // before window — kept
        Assert.Contains("2026-05-01", starts); // after window — kept
        Assert.Contains("2026-06-01", starts);
        Assert.Contains("2026-02-10", starts); // imported
        Assert.Contains("2026-03-10", starts);
        Assert.DoesNotContain("2026-02-15", starts); // replaced

        var preds = await _svc.GetPredictionsAsync(_userId);
        Assert.NotEmpty(preds); // forecast regenerated
    }

    [Fact]
    public async Task Patch_EmptyImport_NoChange()
    {
        SeedCycles("2026-01-01");
        var result = await _svc.PatchCyclesAsync(_userId, []);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.Single(await _svc.GetUserCyclesAsync(_userId));
    }

    // --- Naming convention ----------------------------------------------------

    [Fact]
    public void ExportFileName_Full_NoUser()
    {
        var doc = new ExportDocument
        {
            Kind = "export",
            Scope = "full",
            ExportedAt = new DateTime(2026, 6, 13, 9, 0, 0, DateTimeKind.Utc),
            Range = new ExportRange { Start = "2021-01-01", End = "2026-06-13" },
        };
        Assert.Equal("thosedays-export-full-20210101_20260613-20260613T0900Z.json",
            CycleService.ExportFileName(doc));
    }

    [Fact]
    public void ExportFileName_Backup_WithUserSegment()
    {
        var doc = new ExportDocument
        {
            Kind = "backup",
            Scope = "patch",
            ExportedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Range = new ExportRange { Start = "2026-05-01", End = "2026-06-05" },
        };
        Assert.Equal("thosedays-backup-abc12345-patch-20260501_20260605-20260601T0000Z.json",
            CycleService.ExportFileName(doc, "abc12345"));
    }
}
