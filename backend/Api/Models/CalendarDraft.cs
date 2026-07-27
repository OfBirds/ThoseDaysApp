namespace Api.Models;

/// <summary>
/// Per-user server copy of the calendar draft: painted day ISO dates saved before
/// Recalculate. Deliberately inert — never read by stats or forecasting; Recalculate
/// commits the day-set and clears it.
/// </summary>
public class CalendarDraft
{
    public Guid UserId { get; set; }
    public string DaysJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
    public User User { get; set; } = null!;
}
