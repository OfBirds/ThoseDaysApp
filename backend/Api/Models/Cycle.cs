namespace Api.Models;

public class Cycle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required DateTime StartDate { get; set; }
    public int DurationDays { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Corrected { get; set; } = false;

    /// <summary>
    /// True when this cycle was auto-filled by reconcile (an elapsed forecast that
    /// passed its date). Real data that still feeds averages; cleared when the user
    /// edits the entry, making it user-confirmed.
    /// </summary>
    public bool Auto { get; set; } = false;

    /// <summary>
    /// For auto-filled cycles: the date that was originally forecast for this period,
    /// preserved when the forecast became real. Lets the stats page measure how far
    /// predictions landed from reality once the user corrects an entry. Null for
    /// user-logged cycles that never came from a prediction.
    /// </summary>
    public DateTime? PredictedStart { get; set; }

    public User? User { get; set; }
}
