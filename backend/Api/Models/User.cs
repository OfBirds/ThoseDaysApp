namespace Api.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }

    /// <summary>
    /// PBKDF2 hash for the local (backup) login. Null for accounts created via
    /// CrimsonRaven OIDC, which have no local password.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// The CrimsonRaven (OIDC) subject this account is linked to, set on first SSO login
    /// (matched to an existing row by verified email — see <c>OidcUserProvisioner</c>).
    /// Null until linked. The ThoseDays <see cref="Id"/> remains the owner of all data.
    /// </summary>
    public string? ExternalSubject { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether this user receives "new version released" emails.</summary>
    public bool NotifyReleases { get; set; } = true;

    /// <summary>Whether this user receives a reminder email before a predicted period.</summary>
    public bool NotifyPeriodReminder { get; set; } = false;

    /// <summary>How many days before a predicted period to send the reminder.</summary>
    public int ReminderLeadDays { get; set; } = 2;

    /// <summary>
    /// The predicted-start date we last reminded this user about. Dedupes the daily
    /// sweep so one upcoming cycle is reminded at most once; a regenerated forecast
    /// with a new start date re-arms it. Null until the first reminder is sent.
    /// </summary>
    public DateTime? LastReminderSentFor { get; set; }

    /// <summary>Opaque token for one-click unsubscribe links.</summary>
    public Guid UnsubscribeToken { get; set; } = Guid.NewGuid();

    public ICollection<Cycle> Cycles { get; set; } = [];
    public ICollection<Prediction> Predictions { get; set; } = [];
}
