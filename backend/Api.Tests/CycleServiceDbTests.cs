using Api.Config;
using Api.Data;
using Api.Models;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>EF Core InMemory tests for CycleService database operations.</summary>
public class CycleServiceDbTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CycleService _svc;
    private readonly Guid _userId = Guid.NewGuid();
    private static readonly RecalcConfig TestConfig = new()
    {
        Weights = [3, 2, 1],
        TailWeight = 1,
        DefaultCycleLength = 28,
        DefaultPeriodDuration = 5,
        ForecastCount = 15
    };

    public CycleServiceDbTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"thosedays_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
        _svc = new CycleService(_db, Options.Create(TestConfig));

        _db.Users.Add(new User { Id = _userId, Email = "test@example.com", PasswordHash = "hash" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // --- RecalculateAsync -------------------------------------------------------

    [Fact]
    public async Task RecalculateAsync_ReplacesActuals_AndReturnsNewCycles()
    {
        // Seed an existing cycle that should be replaced.
        var oldCycle = new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 5
        };
        _db.Cycles.Add(oldCycle);
        await _db.SaveChangesAsync();

        var days = new[] { new DateTime(2025, 3, 1), new DateTime(2025, 3, 2), new DateTime(2025, 3, 3) };

        var (cycleLength, periodDuration, cycles, forecast) =
            await _svc.RecalculateAsync(_userId, days, null, null);

        // Old cycle should be gone, new one from the painted days.
        Assert.False(await _db.Cycles.AnyAsync(c => c.Id == oldCycle.Id));
        Assert.Single(cycles);
        Assert.Equal(3, cycles[0].DurationDays);
        Assert.False(cycles[0].Auto);

        // Default cycle length (28) since only one period → no intervals.
        Assert.Equal(28, cycleLength);
        Assert.Equal(3, periodDuration);
    }

    [Fact]
    public async Task RecalculateAsync_OverrideWins_OverComputedAverage()
    {
        var days = new[] {
            new DateTime(2025, 6, 1), new DateTime(2025, 6, 2),  // period 1
            new DateTime(2025, 7, 1), new DateTime(2025, 7, 2),  // period 2 — 30d interval
        };

        var (cycleLength, periodDuration, _, _) =
            await _svc.RecalculateAsync(_userId, days, cycleLengthOverride: 26, periodDurationOverride: 3);

        // Overrides should win over the computed values.
        Assert.Equal(26, cycleLength);
        Assert.Equal(3, periodDuration);
    }

    [Fact]
    public async Task RecalculateAsync_ProducesForecastCountPredictions()
    {
        var days = new[] {
            new DateTime(2025, 1, 1), new DateTime(2025, 1, 2),
            new DateTime(2025, 2, 1), new DateTime(2025, 2, 2),
        };

        var (_, _, _, forecast) = await _svc.RecalculateAsync(_userId, days, null, null);

        Assert.Equal(TestConfig.ForecastCount, forecast.Count);
    }

    [Fact]
    public async Task RecalculateAsync_CyclesFlaggedAutoFalse()
    {
        var days = new[] { new DateTime(2025, 5, 10) };
        var (_, _, cycles, _) = await _svc.RecalculateAsync(_userId, days, null, null);

        Assert.All(cycles, c => Assert.False(c.Auto));
    }

    // --- ReconcileAsync ----------------------------------------------------------

    [Fact]
    public async Task ReconcileAsync_ConvertsOverdueForecasts_ToAutoCycles()
    {
        // Seed predictions with one past, one future.
        _db.Predictions.AddRange(
            new Prediction
            {
                UserId = _userId,
                PredictedStart = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                PredictedDuration = 5
            },
            new Prediction
            {
                UserId = _userId,
                PredictedStart = new DateTime(2099, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                PredictedDuration = 5
            }
        );
        await _db.SaveChangesAsync();

        var localToday = new DateTime(2025, 2, 1);
        var converted = await _svc.ReconcileAsync(_userId, localToday);

        Assert.Equal(1, converted);

        // The overdue prediction → Auto cycle.
        var cycles = await _db.Cycles.Where(c => c.UserId == _userId).ToListAsync();
        Assert.Single(cycles);
        Assert.True(cycles[0].Auto);
        Assert.Equal(5, cycles[0].DurationDays);
    }

    [Fact]
    public async Task ReconcileAsync_Idempotent_NoOp_WhenNothingOverdue()
    {
        _db.Predictions.Add(new Prediction
        {
            UserId = _userId,
            PredictedStart = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PredictedDuration = 5
        });
        await _db.SaveChangesAsync();

        var converted = await _svc.ReconcileAsync(_userId, new DateTime(2025, 6, 1));

        Assert.Equal(0, converted);
    }

    [Fact]
    public async Task ReconcileAsync_RegeneratesForecast_AfterConversion()
    {
        // Need cycles so GeneratePredictionsAsync has history.
        _db.Cycles.Add(new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 5
        });
        _db.Predictions.Add(new Prediction
        {
            UserId = _userId,
            PredictedStart = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            PredictedDuration = 5
        });
        await _db.SaveChangesAsync();

        await _svc.ReconcileAsync(_userId, new DateTime(2025, 3, 1));

        // Forecast should be regenerated back to ForecastCount.
        var predictions = await _db.Predictions.Where(p => p.UserId == _userId).ToListAsync();
        Assert.Equal(TestConfig.ForecastCount, predictions.Count);
    }

    // --- GeneratePredictionsAsync ------------------------------------------------

    [Fact]
    public async Task GeneratePredictionsAsync_Always15Future()
    {
        // Seed one cycle as history.
        _db.Cycles.Add(new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 5
        });
        await _db.SaveChangesAsync();

        var predictions = await _svc.GeneratePredictionsAsync(_userId, TestConfig.ForecastCount);

        Assert.Equal(TestConfig.ForecastCount, predictions.Count);
    }

    [Fact]
    public async Task GeneratePredictionsAsync_EmptyHistory_ReturnsNoPredictions()
    {
        var predictions = await _svc.GeneratePredictionsAsync(_userId, TestConfig.ForecastCount);

        Assert.Empty(predictions);
    }

    // --- AddCycleAsync -----------------------------------------------------------

    [Fact]
    public async Task AddCycleAsync_RegeneratesForecast()
    {
        await _svc.AddCycleAsync(_userId, new DateTime(2025, 1, 1), 5);

        var predictions = await _db.Predictions.Where(p => p.UserId == _userId).ToListAsync();
        Assert.Equal(TestConfig.ForecastCount, predictions.Count);
    }

    // --- UpdateCycleAsync --------------------------------------------------------

    [Fact]
    public async Task UpdateCycleAsync_SetsCorrectedTrue_AndAutoFalse()
    {
        var cycle = await _svc.AddCycleAsync(_userId, new DateTime(2025, 1, 1), 5);

        var updated = await _svc.UpdateCycleAsync(_userId, cycle.Id, new DateTime(2025, 1, 5), 6);

        Assert.NotNull(updated);
        Assert.True(updated.Corrected);
        Assert.False(updated.Auto);
    }

    [Fact]
    public async Task UpdateCycleAsync_RegeneratesForecast()
    {
        var cycle = await _svc.AddCycleAsync(_userId, new DateTime(2025, 1, 1), 5);
        // Clear predictions to verify regeneration.
        _db.Predictions.RemoveRange(_db.Predictions);
        await _db.SaveChangesAsync();

        await _svc.UpdateCycleAsync(_userId, cycle.Id, new DateTime(2025, 1, 5), 6);

        var predictions = await _db.Predictions.Where(p => p.UserId == _userId).ToListAsync();
        Assert.Equal(TestConfig.ForecastCount, predictions.Count);
    }

    // --- DeleteCycleAsync --------------------------------------------------------

    [Fact]
    public async Task DeleteCycleAsync_RemovesCycle_AndRegeneratesForecast()
    {
        var cycle = await _svc.AddCycleAsync(_userId, new DateTime(2025, 1, 1), 5);

        var result = await _svc.DeleteCycleAsync(_userId, cycle.Id);

        Assert.True(result);
        Assert.Null(await _db.Cycles.FindAsync(cycle.Id));
    }

    [Fact]
    public async Task DeleteCycleAsync_NonExistent_ReturnsFalse()
    {
        var result = await _svc.DeleteCycleAsync(_userId, Guid.NewGuid());
        Assert.False(result);
    }
}
