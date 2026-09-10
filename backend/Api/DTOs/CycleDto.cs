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

    /// <summary>
    /// The committed days the user was shown and accepted losing (ISO yyyy-MM-dd), echoed back
    /// from the 409. A commit that would delete a day not in this list is refused (409) and the
    /// current list returned — so days committed while the dialog was open are never deleted
    /// unseen. Empty on the first attempt.
    /// </summary>
    public List<string> ConfirmedRemovals { get; set; } = [];
}

/// <summary>409 body: the committed days this Recalculate would delete.</summary>
public class RecalcConflictResponse
{
    public string Error { get; set; } = "confirm_removals_required";
    public List<string> DroppedDays { get; set; } = [];
}

public class RecalcResponse
{
    public int CycleLength { get; set; }
    public int PeriodDuration { get; set; }
    public List<CycleResponse> Cycles { get; set; } = [];
    public List<PredictionResponse> Forecast { get; set; } = [];
}
