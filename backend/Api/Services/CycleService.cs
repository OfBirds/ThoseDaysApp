using Api.Config;
using Api.Data;
using Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Services;

public interface ICycleService
{
    Task<List<Cycle>> GetUserCyclesAsync(Guid userId);
    Task<List<Prediction>> GetPredictionsAsync(Guid userId);
    Task<Cycle> AddCycleAsync(Guid userId, DateTime startDate, int durationDays);
    Task<Cycle?> UpdateCycleAsync(Guid userId, Guid cycleId, DateTime startDate, int durationDays);
    Task<bool> DeleteCycleAsync(Guid userId, Guid cycleId);
    Task<(double averageCycleLength, double averageInterval, int totalPeriods)> GetStatsAsync(Guid userId);
    Task<List<Prediction>> GeneratePredictionsAsync(Guid userId, int numCycles);
    Task<(int cycleLength, int periodDuration, List<Cycle> cycles, List<Prediction> forecast)> RecalculateAsync(
        Guid userId, IEnumerable<DateTime> days, int? cycleLengthOverride, int? periodDurationOverride);
    Task<int> ReconcileAsync(Guid userId, DateTime localToday);
}

public class CycleService(AppDbContext context, IOptions<RecalcConfig> configOptions) : ICycleService
{
    private readonly RecalcConfig config = configOptions.Value;

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public async Task<List<Cycle>> GetUserCyclesAsync(Guid userId)
    {
        return await context.Cycles
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.StartDate)
            .ToListAsync();
    }

    public async Task<List<Prediction>> GetPredictionsAsync(Guid userId)
    {
        return await context.Predictions
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.PredictedStart)
            .ToListAsync();
    }

    public async Task<Cycle> AddCycleAsync(Guid userId, DateTime startDate, int durationDays)
    {
        var cycle = new Cycle
        {
            UserId = userId,
            StartDate = ToUtc(startDate),
            DurationDays = durationDays
        };

        context.Cycles.Add(cycle);
        await context.SaveChangesAsync();

        await GeneratePredictionsAsync(userId, config.ForecastCount);

        return cycle;
    }

    public async Task<Cycle?> UpdateCycleAsync(Guid userId, Guid cycleId, DateTime startDate, int durationDays)
    {
        var cycle = await context.Cycles.FirstOrDefaultAsync(c => c.Id == cycleId && c.UserId == userId);
        if (cycle == null)
            return null;

        cycle.StartDate = ToUtc(startDate);
        cycle.DurationDays = durationDays;
        cycle.Corrected = true;
        cycle.Auto = false; // a user edit confirms the entry

        await context.SaveChangesAsync();

        await GeneratePredictionsAsync(userId, config.ForecastCount);

        return cycle;
    }

    public async Task<bool> DeleteCycleAsync(Guid userId, Guid cycleId)
    {
        var cycle = await context.Cycles.FirstOrDefaultAsync(c => c.Id == cycleId && c.UserId == userId);
        if (cycle == null)
            return false;

        context.Cycles.Remove(cycle);
        await context.SaveChangesAsync();

        await GeneratePredictionsAsync(userId, config.ForecastCount);

        return true;
    }

    /// <summary>
    /// Returns (averageCycleLength = period duration, averageInterval = cycle length,
    /// totalPeriods). Both averages are recent-favored weighted means.
    /// </summary>
    public async Task<(double averageCycleLength, double averageInterval, int totalPeriods)> GetStatsAsync(Guid userId)
    {
        var periods = await GetPeriodsAsync(userId);
        if (periods.Count == 0)
            return (0, 0, 0);

        var (cycleLength, periodDuration) = ComputeAverages(periods);
        return (periodDuration, cycleLength, periods.Count);
    }

    public async Task<List<Prediction>> GeneratePredictionsAsync(Guid userId, int numCycles)
    {
        var periods = await GetPeriodsAsync(userId);
        var (cycleLength, periodDuration) = ComputeAverages(periods);

        DateTime? lastStart = periods.Count > 0 ? periods[^1].Start : null;
        return await RegenerateForecastAsync(userId, lastStart, cycleLength, periodDuration, numCycles);
    }

    public async Task<(int cycleLength, int periodDuration, List<Cycle> cycles, List<Prediction> forecast)> RecalculateAsync(
        Guid userId, IEnumerable<DateTime> days, int? cycleLengthOverride, int? periodDurationOverride)
    {
        // Replace the user's actuals with the committed painted day-set.
        var existing = await context.Cycles.Where(c => c.UserId == userId).ToListAsync();
        context.Cycles.RemoveRange(existing);

        var periods = GroupDaysIntoPeriods(days.Select(d => ToUtc(d).Date));

        var newCycles = periods.Select(p => new Cycle
        {
            UserId = userId,
            StartDate = DateTime.SpecifyKind(p.Start, DateTimeKind.Utc),
            DurationDays = p.Length,
            Auto = false
        }).ToList();

        context.Cycles.AddRange(newCycles);
        // NOTE: deliberately no SaveChanges here. The cycle removals/adds stay tracked
        // and are flushed together with the prediction changes by the single
        // SaveChanges inside RegenerateForecastAsync — one atomic, batched commit.

        // Override wins; otherwise use the weighted average.
        var (avgCycleLength, avgPeriodDuration) = ComputeAverages(periods);
        var cycleLength = cycleLengthOverride ?? avgCycleLength;
        var periodDuration = periodDurationOverride ?? avgPeriodDuration;

        DateTime? lastStart = periods.Count > 0 ? periods[^1].Start : null;
        var forecast = await RegenerateForecastAsync(userId, lastStart, cycleLength, periodDuration, config.ForecastCount);

        return (cycleLength, periodDuration, newCycles, forecast);
    }

    /// <summary>
    /// Catch-up loop: every forecast whose start date is before the user's local
    /// today becomes an Auto (real) Cycle, then the forecast is regenerated so the
    /// cadence continues. Idempotent — a no-op when nothing is overdue.
    /// Returns the number of forecasts converted.
    /// </summary>
    public async Task<int> ReconcileAsync(Guid userId, DateTime localToday)
    {
        // Npgsql requires a UTC Kind for timestamptz comparisons; .Date strips it.
        var todayDate = DateTime.SpecifyKind(ToUtc(localToday).Date, DateTimeKind.Utc);

        var elapsed = await context.Predictions
            .Where(p => p.UserId == userId && p.PredictedStart < todayDate)
            .OrderBy(p => p.PredictedStart)
            .ToListAsync();

        if (elapsed.Count == 0)
            return 0;

        foreach (var p in elapsed)
        {
            context.Cycles.Add(new Cycle
            {
                UserId = userId,
                StartDate = DateTime.SpecifyKind(p.PredictedStart.Date, DateTimeKind.Utc),
                DurationDays = p.PredictedDuration,
                Auto = true,
                PredictedStart = DateTime.SpecifyKind(p.PredictedStart.Date, DateTimeKind.Utc)
            });
        }
        context.Predictions.RemoveRange(elapsed);
        await context.SaveChangesAsync();

        // Regenerate so there are always ForecastCount future periods.
        await GeneratePredictionsAsync(userId, config.ForecastCount);

        return elapsed.Count;
    }

    private async Task<List<Prediction>> RegenerateForecastAsync(
        Guid userId, DateTime? lastStart, int cycleLength, int periodDuration, int numCycles)
    {
        var existing = await context.Predictions.Where(p => p.UserId == userId).ToListAsync();
        context.Predictions.RemoveRange(existing);

        var predictions = new List<Prediction>();
        if (lastStart is null)
        {
            await context.SaveChangesAsync();
            return predictions;
        }

        if (cycleLength < 1) cycleLength = config.DefaultCycleLength;
        if (periodDuration < 1) periodDuration = config.DefaultPeriodDuration;

        var nextStart = lastStart.Value.AddDays(cycleLength);
        for (int i = 0; i < numCycles; i++)
        {
            predictions.Add(new Prediction
            {
                UserId = userId,
                PredictedStart = DateTime.SpecifyKind(nextStart, DateTimeKind.Utc),
                PredictedDuration = periodDuration,
                Confidence = 0.85f
            });
            nextStart = nextStart.AddDays(cycleLength);
        }

        context.Predictions.AddRange(predictions);
        await context.SaveChangesAsync();
        return predictions;
    }

    /// <summary>Weighted cycle length (interval) and period duration from the actuals.</summary>
    internal (int cycleLength, int periodDuration) ComputeAverages(List<(DateTime Start, int Length)> periods)
    {
        if (periods.Count == 0)
            return (config.DefaultCycleLength, config.DefaultPeriodDuration);

        // Durations, newest first.
        var durations = new List<int>();
        for (int i = periods.Count - 1; i >= 0; i--)
            durations.Add(periods[i].Length);

        // Intervals between consecutive starts, newest first.
        var intervals = new List<int>();
        for (int i = periods.Count - 1; i >= 1; i--)
            intervals.Add((periods[i].Start - periods[i - 1].Start).Days);

        var cycleLength = WeightedAverage(intervals, config.DefaultCycleLength);
        var periodDuration = WeightedAverage(durations, config.DefaultPeriodDuration);
        return (cycleLength, periodDuration);
    }

    /// <summary>Recent-favored weighted mean, rounded. Values ordered newest → oldest.</summary>
    internal int WeightedAverage(List<int> valuesNewestFirst, int fallback)
    {
        if (valuesNewestFirst.Count == 0)
            return fallback;

        double weightedSum = 0;
        double weightTotal = 0;
        for (int i = 0; i < valuesNewestFirst.Count; i++)
        {
            int w = i < config.Weights.Length ? config.Weights[i] : config.TailWeight;
            weightedSum += valuesNewestFirst[i] * w;
            weightTotal += w;
        }

        return weightTotal > 0 ? (int)Math.Round(weightedSum / weightTotal) : fallback;
    }

    private async Task<List<(DateTime Start, int Length)>> GetPeriodsAsync(Guid userId)
    {
        var cycles = await GetUserCyclesAsync(userId);
        var days = new List<DateTime>();
        foreach (var c in cycles)
            for (int i = 0; i < c.DurationDays; i++)
                days.Add(c.StartDate.Date.AddDays(i));

        return GroupDaysIntoPeriods(days);
    }

    /// <summary>Collapse a set of individual days into consecutive-day periods.</summary>
    internal static List<(DateTime Start, int Length)> GroupDaysIntoPeriods(IEnumerable<DateTime> days)
    {
        var sorted = new SortedSet<DateTime>(days.Select(d => d.Date));

        var periods = new List<(DateTime Start, int Length)>();
        DateTime? runStart = null;
        DateTime? prev = null;
        int length = 0;

        foreach (var day in sorted)
        {
            if (runStart is null)
            {
                runStart = day;
                length = 1;
            }
            else if (day == prev!.Value.AddDays(1))
            {
                length++;
            }
            else
            {
                periods.Add((runStart.Value, length));
                runStart = day;
                length = 1;
            }
            prev = day;
        }

        if (runStart is not null)
            periods.Add((runStart.Value, length));

        return periods;
    }
}
