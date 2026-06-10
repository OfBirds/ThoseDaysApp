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

    public User? User { get; set; }
}
