namespace Api.DTOs;

/// <summary>
/// User-facing preferences served to / accepted from the settings page. Starts
/// with the existing release-email opt-in; grows as features add prefs (e.g.
/// period reminders).
/// </summary>
public class UserPrefsResponse
{
    public bool NotifyReleases { get; set; }
    public bool NotifyPeriodReminder { get; set; }
    public int ReminderLeadDays { get; set; }
}

public class UpdateUserPrefsRequest
{
    public bool NotifyReleases { get; set; }
    public bool NotifyPeriodReminder { get; set; }
    public int ReminderLeadDays { get; set; }
}
