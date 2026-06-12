namespace Api.DTOs;

public class CreateCycleRequest
{
    public required DateTime StartDate { get; set; }
    public int DurationDays { get; set; }
}

public class UpdateCycleRequest
{
    public DateTime StartDate { get; set; }
    public int DurationDays { get; set; }
}

public class CycleResponse
{
    public Guid Id { get; set; }
    public DateTime StartDate { get; set; }
    public int DurationDays { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Corrected { get; set; }
    public bool Auto { get; set; }
    public DateTime? PredictedStart { get; set; }
}

public class PredictionResponse
{
    public Guid Id { get; set; }
    public DateTime PredictedStart { get; set; }
    public int PredictedDuration { get; set; }
    public float Confidence { get; set; }
}

public class StatsResponse
{
    public double AverageCycleLength { get; set; }
    public double AverageInterval { get; set; }
    public int TotalCycles { get; set; }
}

public class PredictionRequest
{
    public int Cycles { get; set; } = 15;
}

/// <summary>
/// The committed draft sent on Recalculate: the full set of painted period days
/// (ISO yyyy-MM-dd local dates) plus optional user overrides of the two averages.
/// </summary>
public class RecalcRequest
{
    public List<string> Days { get; set; } = [];
    public int? CycleLength { get; set; }
    public int? PeriodDuration { get; set; }
}

public class RecalcResponse
{
    public int CycleLength { get; set; }
    public int PeriodDuration { get; set; }
    public List<CycleResponse> Cycles { get; set; } = [];
    public List<PredictionResponse> Forecast { get; set; } = [];
}
