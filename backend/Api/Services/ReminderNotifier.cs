using Api.Config;
using Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>
/// Periodically (every few hours) emails opted-in users a gentle heads-up a few
/// days before their next predicted period. Gated by NOTIFY_REMINDERS so
/// staging/local stay quiet, and no-ops cleanly when SMTP isn't configured.
/// Dedupes via User.LastReminderSentFor so one upcoming cycle is reminded once.
/// </summary>
public class ReminderNotifier(
    IServiceScopeFactory scopeFactory,
    IEmailSender email,
    IOptions<SmtpOptions> smtpOptions,
    IConfiguration config,
    ILogger<ReminderNotifier> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var enabled = string.Equals(config["NOTIFY_REMINDERS"], "true",
            StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            logger.LogInformation("Period reminders disabled (NOTIFY_REMINDERS not true).");
            return;
        }

        // A startup sweep covers the "container just restarted" case, then we tick.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSweepAsync(DateTime.UtcNow, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder sweep failed.");
            }

            try
            {
                await Task.Delay(SweepInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Pure eligibility check: due when the next predicted start is within the lead
    /// window [today, today + leadDays] and we haven't already reminded for that exact
    /// start date. Time-injected so it's unit-testable without the loop.
    /// </summary>
    internal static bool IsDue(DateTime? nextPredictedStart, DateTime today, int leadDays,
        DateTime? lastReminderSentFor)
    {
        if (nextPredictedStart is null)
            return false;

        var start = nextPredictedStart.Value.Date;
        var daysUntil = (start - today.Date).Days;
        if (daysUntil < 0 || daysUntil > leadDays)
            return false;

        // Already reminded for this exact upcoming cycle.
        if (lastReminderSentFor?.Date == start)
            return false;

        return true;
    }

    /// <summary>
    /// Runs one sweep at the given "now". Returns how many reminders were sent.
    /// Best-effort: a failed send is logged and does not advance the dedupe marker
    /// (so it retries next sweep) or block other recipients.
    /// </summary>
    public async Task<int> RunSweepAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (!smtpOptions.Value.IsConfigured)
        {
            logger.LogInformation("SMTP not configured; reminder sweep is a no-op.");
            return 0;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var baseUrl = (config["PUBLIC_BASE_URL"] ?? "").TrimEnd('/');
        var today = DateTime.SpecifyKind(nowUtc.Date, DateTimeKind.Utc);

        var candidates = await db.Users
            .Where(u => u.IsActive && u.NotifyPeriodReminder)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var u in candidates)
        {
            var next = await db.Predictions
                .Where(p => p.UserId == u.Id && p.PredictedStart >= today)
                .OrderBy(p => p.PredictedStart)
                .FirstOrDefaultAsync(ct);

            if (!IsDue(next?.PredictedStart, today, u.ReminderLeadDays, u.LastReminderSentFor))
                continue;

            var daysUntil = (next!.PredictedStart.Date - today.Date).Days;
            var unsubscribe = $"{baseUrl}/api/unsubscribe?token={u.UnsubscribeToken}&kind=reminder";
            var (subject, html, text) = BuildReminderEmail(next.PredictedStart, daysUntil, baseUrl, unsubscribe);

            try
            {
                await email.SendAsync(u.Email, subject, html, text, ct);
                u.LastReminderSentFor = DateTime.SpecifyKind(next.PredictedStart.Date, DateTimeKind.Utc);
                sent++;
                logger.LogInformation("Reminder sent to {Recipient} for {Start}", u.Email, next.PredictedStart.Date);
            }
            catch (Exception ex)
            {
                // Leave LastReminderSentFor untouched so the next sweep retries.
                logger.LogError(ex, "Reminder email FAILED for {Recipient}", u.Email);
            }
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    internal static (string subject, string html, string text) BuildReminderEmail(
        DateTime predictedStart, int daysUntil, string url, string unsubscribe)
    {
        var when = daysUntil == 0 ? "today"
            : daysUntil == 1 ? "tomorrow"
            : $"in {daysUntil} days";
        var dateStr = predictedStart.ToString("dddd, d MMM yyyy");
        var subject = $"ThoseDays — your next period is expected {when}";

        var html = $"""
            <p>Hi,</p>
            <p>Your next period is expected <strong>{when}</strong> — around <strong>{dateStr}</strong>.</p>
            <p>This is just a gentle heads-up based on your recent cycles; it may come a little
            earlier or later.</p>
            <p><a href="{url}">Open ThoseDays</a></p>
            <hr>
            <p style="font-size:12px;color:#888">
              Don't want these reminders? <a href="{unsubscribe}">Unsubscribe</a>.
            </p>
            """;
        var text =
            $"Your next period is expected {when} — around {dateStr}.\n\n" +
            "This is a gentle heads-up based on your recent cycles; it may come a little earlier or later.\n\n" +
            $"{url}\n\n" +
            $"Unsubscribe from reminders: {unsubscribe}";
        return (subject, html, text);
    }
}
