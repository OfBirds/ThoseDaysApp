using Api.Config;
using Api.Data;
using Api.Models;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>Server-side calendar draft: inert storage, cleared when Recalculate commits.</summary>
public class CalendarDraftTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CycleService _svc;
    private readonly Guid _userId = Guid.NewGuid();

    public CalendarDraftTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"thosedays_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
        _svc = new CycleService(_db, Options.Create(new RecalcConfig
        {
            Weights = [3, 2, 1],
            TailWeight = 1,
            DefaultCycleLength = 28,
            DefaultPeriodDuration = 5,
            ForecastCount = 15
        }));

        _db.Users.Add(new User { Id = _userId, Email = "draft@example.com", PasswordHash = "hash" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RecalculateAsync_ClearsSavedDraft()
    {
        _db.CalendarDrafts.Add(new CalendarDraft
        {
            UserId = _userId,
            DaysJson = """["2026-01-01"]""",
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _svc.RecalculateAsync(_userId, [new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)], null, null, confirmedRemovals: []);

        Assert.Empty(_db.CalendarDrafts.Where(d => d.UserId == _userId));
    }

    /// <summary>
    /// The September 2026 data-loss bug: a draft saved before an auto-fill reconcile does
    /// not contain the days reconcile committed, so committing it deleted a whole month.
    /// Reconcile must fold the auto-filled days into the saved draft.
    /// </summary>
    [Fact]
    public async Task ReconcileAsync_FoldsAutoFilledDaysIntoSavedDraft()
    {
        // A period committed in June, and a forecast for August that is now overdue.
        _db.Cycles.Add(new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2026, 6, 19, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 3
        });
        _db.Predictions.Add(new Prediction
        {
            UserId = _userId,
            PredictedStart = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            PredictedDuration = 4,
            Confidence = 1
        });
        // The draft was saved before the August period existed.
        _db.CalendarDrafts.Add(new CalendarDraft
        {
            UserId = _userId,
            DaysJson = """["2026-06-19","2026-06-20","2026-06-21"]""",
            UpdatedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await _db.SaveChangesAsync();

        await _svc.ReconcileAsync(_userId, new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));

        var draft = _db.CalendarDrafts.Single(d => d.UserId == _userId);
        var days = System.Text.Json.JsonSerializer.Deserialize<List<string>>(draft.DaysJson)!;

        // The auto-filled August days are now in the draft, so committing it keeps them.
        Assert.Contains("2026-08-12", days);
        Assert.Contains("2026-08-13", days);
        Assert.Contains("2026-08-14", days);
        Assert.Contains("2026-08-15", days);
        Assert.Contains("2026-06-19", days);
    }

    [Fact]
    public async Task ReconcileAsync_WithoutDraft_CreatesNone()
    {
        _db.Predictions.Add(new Prediction
        {
            UserId = _userId,
            PredictedStart = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            PredictedDuration = 4,
            Confidence = 1
        });
        await _db.SaveChangesAsync();

        await _svc.ReconcileAsync(_userId, new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));

        Assert.Empty(_db.CalendarDrafts.Where(d => d.UserId == _userId));
    }

    [Fact]
    public async Task RecalculateAsync_DroppingCommittedDays_NeedsConfirmation()
    {
        _db.Cycles.Add(new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 4
        });
        await _db.SaveChangesAsync();

        // Commit a day-set that keeps only the first August day — the stale-draft shape.
        var outcome = await _svc.RecalculateAsync(
            _userId, [new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)], null, null, confirmedRemovals: []);

        Assert.False(outcome.IsCommitted);
        Assert.Equal(["2026-08-13", "2026-08-14", "2026-08-15"], outcome.DroppedDays);

        // Nothing was written.
        Assert.Equal(4, _db.Cycles.Single(c => c.UserId == _userId).DurationDays);
    }

    [Fact]
    public async Task RecalculateAsync_DroppingCommittedDays_ProceedsOnceConfirmed()
    {
        _db.Cycles.Add(new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 4
        });
        await _db.SaveChangesAsync();

        var outcome = await _svc.RecalculateAsync(
            _userId, [new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)], null, null, confirmedRemovals: ["2026-08-13", "2026-08-14", "2026-08-15"]);

        Assert.True(outcome.IsCommitted);
        Assert.Equal(1, _db.Cycles.Single(c => c.UserId == _userId).DurationDays);
    }

    [Fact]
    public async Task RecalculateAsync_AddingDaysOnly_NeedsNoConfirmation()
    {
        _db.Cycles.Add(new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 2
        });
        await _db.SaveChangesAsync();

        var outcome = await _svc.RecalculateAsync(
            _userId,
            [
                new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
            ],
            null, null, confirmedRemovals: []);

        Assert.True(outcome.IsCommitted);
        Assert.Empty(outcome.DroppedDays);
    }

    /// <summary>
    /// A reviewer's finding: confirming a removal used to be a bare boolean, so days committed
    /// while the dialog was open (another device, an auto-fill) were deleted without ever being
    /// shown. The confirmation names the days, and anything outside it re-prompts.
    /// </summary>
    [Fact]
    public async Task RecalculateAsync_DaysCommittedAfterTheDialogOpened_RepromptsInsteadOfDeleting()
    {
        _db.Cycles.Add(new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 4
        });
        // Committed after the user was shown the August dates.
        _db.Cycles.Add(new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 2
        });
        await _db.SaveChangesAsync();

        var outcome = await _svc.RecalculateAsync(
            _userId, [new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)], null, null,
            confirmedRemovals: ["2026-08-13", "2026-08-14", "2026-08-15"]);

        Assert.False(outcome.IsCommitted);
        Assert.Contains("2026-09-08", outcome.DroppedDays);
        Assert.Contains("2026-09-09", outcome.DroppedDays);

        // Nothing was deleted.
        Assert.Equal(2, _db.Cycles.Count(c => c.UserId == _userId));
    }

    /// <summary>
    /// A reviewer's finding: DeleteCycleAsync left the saved draft describing a period that no
    /// longer exists. Recalculating that draft re-created it, and because the days were no longer
    /// committed there was nothing for the 409 guard to catch.
    /// </summary>
    [Fact]
    public async Task DeleteCycleAsync_ClearsSavedDraft()
    {
        var cycle = new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 2
        };
        _db.Cycles.Add(cycle);
        _db.CalendarDrafts.Add(new CalendarDraft
        {
            UserId = _userId,
            DaysJson = """["2026-08-12","2026-08-13"]""",
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _svc.DeleteCycleAsync(_userId, cycle.Id);

        Assert.Empty(_db.CalendarDrafts.Where(d => d.UserId == _userId));
    }

    [Fact]
    public async Task UpdateCycleAsync_ClearsSavedDraft()
    {
        var cycle = new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 2
        };
        _db.Cycles.Add(cycle);
        _db.CalendarDrafts.Add(new CalendarDraft
        {
            UserId = _userId,
            DaysJson = """["2026-08-12","2026-08-13"]""",
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _svc.UpdateCycleAsync(_userId, cycle.Id, new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), 3);

        Assert.Empty(_db.CalendarDrafts.Where(d => d.UserId == _userId));
    }

    [Fact]
    public async Task AddCycleAsync_ClearsSavedDraft()
    {
        _db.CalendarDrafts.Add(new CalendarDraft
        {
            UserId = _userId,
            DaysJson = """["2026-08-12"]""",
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _svc.AddCycleAsync(_userId, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 3);

        Assert.Empty(_db.CalendarDrafts.Where(d => d.UserId == _userId));
    }

    [Fact]
    public async Task RecalculateAsync_NoDraft_StillSucceeds()
    {
        var outcome = await _svc.RecalculateAsync(
            _userId, [new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)], null, null, confirmedRemovals: []);

        Assert.Single(outcome.Cycles);
        Assert.True(outcome.CycleLength > 0);
    }
}
