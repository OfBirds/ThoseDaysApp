namespace Api.Config;

/// <summary>
/// All tunable recalculation constants in one place. Bound from the "Recalc"
/// section of appsettings.json and served to the frontend via GET /api/config,
/// so the formula can be changed without touching logic.
/// </summary>
public class RecalcConfig
{
    /// <summary>
    /// Weighted-average weights, newest value first. weights[0] applies to the
    /// most recent cycle, weights[1] to the next, etc. Values older than this
    /// array use <see cref="TailWeight"/>.
    /// </summary>
    // NOTE: left empty by default on purpose. The .NET config binder *appends* to a
    // pre-initialized array, so a default like [3,2,1] + appsettings [3,2,1] yields
    // [3,2,1,3,2,1]. Values come from the "Recalc" section; empty → flat mean fallback.
    public int[] Weights { get; set; } = [];

    /// <summary>Weight applied to values older than <see cref="Weights"/>.</summary>
    public int TailWeight { get; set; } = 1;

    /// <summary>Cycle length (start-to-start) used when there isn't enough history.</summary>
    public int DefaultCycleLength { get; set; } = 28;

    /// <summary>Period duration (bleeding days) used when there isn't enough history.</summary>
    public int DefaultPeriodDuration { get; set; } = 5;

    public int CycleLengthMin { get; set; } = 21;
    public int CycleLengthMax { get; set; } = 35;
    public int PeriodDurationMin { get; set; } = 2;
    public int PeriodDurationMax { get; set; } = 10;

    /// <summary>How many future periods to project.</summary>
    public int ForecastCount { get; set; } = 15;
}
