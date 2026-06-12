using Api.Config;
using Api.Services;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>Pure algorithm tests for CycleService's internal static methods — no DB.</summary>
public class CycleServiceLogicTests
{
    // Helper: create a vanilla RecalcConfig for testing.
    private static IOptions<RecalcConfig> ConfigWith(
        int[]? weights = null, int tailWeight = 1,
        int defaultCycle = 28, int defaultDuration = 5)
    {
        var cfg = new RecalcConfig
        {
            Weights = weights ?? [3, 2, 1],
            TailWeight = tailWeight,
            DefaultCycleLength = defaultCycle,
            DefaultPeriodDuration = defaultDuration
        };
        return Options.Create(cfg);
    }

    // --- GroupDaysIntoPeriods --------------------------------------------------

    [Fact]
    public void GroupDaysIntoPeriods_ConsecutiveRun_ReturnsOnePeriod()
    {
        var days = new[] {
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 4, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = CycleService.GroupDaysIntoPeriods(days);

        var single = Assert.Single(result);
        Assert.Equal(new DateTime(2025, 6, 1), single.Start);
        Assert.Equal(4, single.Length);
    }

    [Fact]
    public void GroupDaysIntoPeriods_GapSplitsIntoTwo()
    {
        var days = new[] {
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc),
            // gap
            new DateTime(2025, 6, 5, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = CycleService.GroupDaysIntoPeriods(days);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Length); // Jun 1-2
        Assert.Equal(1, result[1].Length); // Jun 5
    }

    [Fact]
    public void GroupDaysIntoPeriods_SingleIsolatedDay_LengthOne()
    {
        var days = new[] { new DateTime(2025, 7, 4, 0, 0, 0, DateTimeKind.Utc) };

        var result = CycleService.GroupDaysIntoPeriods(days);

        var single = Assert.Single(result);
        Assert.Equal(1, single.Length);
    }

    [Fact]
    public void GroupDaysIntoPeriods_UnsortedAndDuplicate_Normalized()
    {
        var days = new[] {
            new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), // duplicate
            new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = CycleService.GroupDaysIntoPeriods(days);

        // Sorted & deduped: Jun 1, 2, 3 → one period of length 3
        var single = Assert.Single(result);
        Assert.Equal(new DateTime(2025, 6, 1), single.Start);
        Assert.Equal(3, single.Length);
    }

    [Fact]
    public void GroupDaysIntoPeriods_Empty_ReturnsEmpty()
    {
        var result = CycleService.GroupDaysIntoPeriods([]);

        Assert.Empty(result);
    }

    // --- WeightedAverage --------------------------------------------------------

    [Fact]
    public void WeightedAverage_EmptyValues_ReturnsFallback()
    {
        var svc = new CycleService(null!, ConfigWith());
        var result = svc.WeightedAverage([], 28);
        Assert.Equal(28, result);
    }

    [Fact]
    public void WeightedAverage_RecentFavoredWeights()
    {
        // Weights [3,2,1], values [10, 20] (10 most recent)
        // sum = 10*3 + 20*2 = 70, total = 5, avg = 14
        var svc = new CycleService(null!, ConfigWith(weights: [3, 2, 1]));
        var result = svc.WeightedAverage([10, 20], 0);
        Assert.Equal(14, result);
    }

    [Fact]
    public void WeightedAverage_TailWeight_AppliedBeyondWeightsArray()
    {
        // Weights [3,2], TailWeight=1, values [10, 20, 30, 40] (10 newest)
        // sum = 10*3 + 20*2 + 30*1 + 40*1 = 30+40+30+40 = 140, total = 3+2+1+1 = 7
        // avg = 20
        var svc = new CycleService(null!, ConfigWith(weights: [3, 2], tailWeight: 1));
        var result = svc.WeightedAverage([10, 20, 30, 40], 0);
        Assert.Equal(20, result);
    }

    [Fact]
    public void WeightedAverage_FlatMean_WhenWeightsEmpty()
    {
        // Empty Weights + TailWeight=1 → every value gets weight 1 → flat mean
        var svc = new CycleService(null!, ConfigWith(weights: [], tailWeight: 1));
        var result = svc.WeightedAverage([10, 20, 30], 0);
        Assert.Equal(20, result);
    }

    [Fact]
    public void WeightedAverage_BankersRounding_Edge()
    {
        // Math.Round uses banker's rounding: 27.5 → 28 (ties to even)
        // Weights [1,1], values [27, 28] → (27+28)/2 = 27.5 → 28
        var svc = new CycleService(null!, ConfigWith(weights: [1, 1]));
        var result = svc.WeightedAverage([27, 28], 0);
        Assert.Equal(28, result);
    }

    [Fact]
    public void WeightedAverage_SingleValue()
    {
        var svc = new CycleService(null!, ConfigWith(weights: [3, 2, 1]));
        var result = svc.WeightedAverage([15], 0);
        Assert.Equal(15, result);
    }

    [Fact]
    public void WeightedAverage_ZeroWeightTotal_ReturnsFallback()
    {
        // TailWeight=0 and no weights → everything zeroed out
        var svc = new CycleService(null!, ConfigWith(weights: [], tailWeight: 0));
        var result = svc.WeightedAverage([10, 20], 99);
        Assert.Equal(99, result);
    }

    // --- ComputeAverages --------------------------------------------------------

    [Fact]
    public void ComputeAverages_EmptyPeriods_ReturnsDefaults()
    {
        var svc = new CycleService(null!, ConfigWith(defaultCycle: 28, defaultDuration: 5));
        var (cycleLength, periodDuration) = svc.ComputeAverages([]);

        Assert.Equal(28, cycleLength);
        Assert.Equal(5, periodDuration);
    }

    [Fact]
    public void ComputeAverages_SinglePeriod_IntervalFallsBack()
    {
        // One period: no intervals → cycleLength = default (28)
        // Duration: single value 6 → 6
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = new CycleService(null!, ConfigWith());

        var (cycleLength, periodDuration) = svc.ComputeAverages([(start, 6)]);

        Assert.Equal(28, cycleLength); // no intervals → fallback
        Assert.Equal(6, periodDuration);
    }

    [Fact]
    public void ComputeAverages_MultiplePeriods_ComputesBoth()
    {
        // Periods: Jun 1 (len 5), Jun 30 (len 4) — interval = 29 days
        // Durations newest first: [4, 5] with Weights [3,2,1] → 4*3+5*2=22, /5=4.4→4
        // Intervals newest first: [29] with Weights [3,2,1] → 29*3/3=29
        var p1 = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var p2 = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var svc = new CycleService(null!, ConfigWith());

        var (cycleLength, periodDuration) = svc.ComputeAverages([(p1, 5), (p2, 4)]);

        Assert.Equal(29, cycleLength);
        Assert.Equal(4, periodDuration);
    }
}
