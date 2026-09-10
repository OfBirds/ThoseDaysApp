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

        await _svc.RecalculateAsync(_userId, [new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)], null, null, confirmRemovals: false);

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
            _userId, [new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)], null, null, confirmRemovals: false);

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
            _userId, [new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)], null, null, confirmRemovals: true);

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
            null, null, confirmRemovals: false);

        Assert.True(outcome.IsCommitted);
        Assert.Empty(outcome.DroppedDays);
    }

    [Fact]
    public async Task RecalculateAsync_NoDraft_StillSucceeds()
    {
        var outcome = await _svc.RecalculateAsync(
            _userId, [new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)], null, null, confirmRemovals: false);

        Assert.Single(outcome.Cycles);
        Assert.True(outcome.CycleLength > 0);
    }
}
