using Api.Config;
using Api.Data;
using Api.Models;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>Tests for the variability-based prediction confidence.</summary>
public class PredictionConfidenceTests
{
    private static CycleService Svc() =>
        new(null!, Options.Create(new RecalcConfig
        {
            Weights = [3, 2, 1],
            DefaultCycleLength = 28,
            DefaultPeriodDuration = 5,
            ConfidenceFloor = 0.3,
            ConfidenceNominal = 0.7,
            ConfidenceMinIntervals = 2,
        }));

    private static List<(DateTime Start, int Length)> Periods(params int[] startOffsets) =>
        startOffsets.Select(o => (new DateTime(2025, 1, 1).AddDays(o), 5)).ToList();

    // --- StdDev (static) -------------------------------------------------------

    [Fact]
    public void StdDev_FewerThanTwo_IsZero()
    {
        Assert.Equal(0, CycleService.StdDev([]));
        Assert.Equal(0, CycleService.StdDev([28]));
    }

    [Fact]
    public void StdDev_IdenticalValues_IsZero()
    {
        Assert.Equal(0, CycleService.StdDev([28, 28, 28]));
    }

    [Fact]
    public void StdDev_KnownSpread()
    {
        // values 10, 50 → mean 30, variance 400, stddev 20
        Assert.Equal(20, CycleService.StdDev([10, 50]), 3);
    }

    // --- ComputeConfidence -----------------------------------------------------

    [Fact]
    public void Confidence_RegularHistory_IsHigh()
    {
        // Perfectly regular 28-day cycles → sigma 0 → confidence 1.0
        var periods = Periods(0, 28, 56, 84);
        Assert.Equal(1.0, Svc().ComputeConfidence(periods, 28), 3);
    }

    [Fact]
    public void Confidence_ErraticHistory_ClampedToFloor()
    {
        // intervals 5 then 55 → sigma 25, mu 30 → 1 - 25/30 = 0.167 → clamped to floor 0.3
        var periods = Periods(0, 5, 60);
        Assert.Equal(0.3, Svc().ComputeConfidence(periods, 30), 3);
    }

    [Fact]
    public void Confidence_ThinHistory_FallsBackToNominal()
    {
        // Only one interval (< ConfidenceMinIntervals) → nominal
        var periods = Periods(0, 28);
        Assert.Equal(0.7, Svc().ComputeConfidence(periods, 28), 3);
    }

    [Fact]
    public void Confidence_NoHistory_FallsBackToNominal()
    {
        Assert.Equal(0.7, Svc().ComputeConfidence([], 28), 3);
    }

    // --- End-to-end: forecast carries the computed confidence ------------------

    [Fact]
    public async Task GeneratePredictions_RegularCycles_HighConfidence_NotHardcoded()
    {
        var (svc, userId) = NewDbSvc();
        // Three regular 28-day cycles → two equal intervals → sigma 0.
        await AddCycle(svc, userId, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddCycle(svc, userId, new DateTime(2025, 1, 29, 0, 0, 0, DateTimeKind.Utc));
        await AddCycle(svc, userId, new DateTime(2025, 2, 26, 0, 0, 0, DateTimeKind.Utc));
        var preds = await svc.GeneratePredictionsAsync(userId, 5);

        Assert.NotEmpty(preds);
        Assert.True(preds[0].Confidence > 0.9f);
        Assert.NotEqual(0.85f, preds[0].Confidence); // no longer the old hardcoded value
    }

    private static (CycleService svc, Guid userId) NewDbSvc()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"conf_{Guid.NewGuid()}").Options;
        var db = new AppDbContext(options);
        var userId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Email = "c@e.com", PasswordHash = "h" });
        db.SaveChanges();
        var cfg = Options.Create(new RecalcConfig
        {
            Weights = [3, 2, 1],
            DefaultCycleLength = 28,
            DefaultPeriodDuration = 5,
            ForecastCount = 15,
            ConfidenceFloor = 0.3,
            ConfidenceNominal = 0.7,
            ConfidenceMinIntervals = 2,
        });
        return (new CycleService(db, cfg), userId);
    }

    private static Task AddCycle(CycleService svc, Guid userId, DateTime start) =>
        svc.AddCycleAsync(userId, start, 5);
}
