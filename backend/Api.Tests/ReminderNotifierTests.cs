using Api.Config;
using Api.Data;
using Api.Models;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>Tests for ReminderNotifier — pure IsDue, the sweep, and the email.</summary>
public class ReminderNotifierTests
{
    private static readonly DateTime Today = new(2026, 6, 13, 9, 0, 0, DateTimeKind.Utc);

    private static DateTime Day(int offsetFromToday) =>
        DateTime.SpecifyKind(Today.Date.AddDays(offsetFromToday), DateTimeKind.Utc);

    // --- IsDue (pure) ----------------------------------------------------------

    [Theory]
    [InlineData(0, 2, true)]   // due today, within lead
    [InlineData(1, 2, true)]   // 1 day out
    [InlineData(2, 2, true)]   // exactly at lead boundary
    [InlineData(3, 2, false)]  // beyond lead window
    [InlineData(-1, 2, false)] // in the past
    public void IsDue_LeadWindowBoundaries(int daysOut, int lead, bool expected)
    {
        var result = ReminderNotifier.IsDue(Day(daysOut), Today, lead, lastReminderSentFor: null);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsDue_NoPrediction_NotDue() =>
        Assert.False(ReminderNotifier.IsDue(null, Today, 2, null));

    [Fact]
    public void IsDue_AlreadyRemindedForThatStart_NotDue()
    {
        var start = Day(1);
        Assert.False(ReminderNotifier.IsDue(start, Today, 2, lastReminderSentFor: start));
    }

    [Fact]
    public void IsDue_RemindedForDifferentStart_ReArms()
    {
        var newStart = Day(1);
        var oldStart = Day(-30);
        Assert.True(ReminderNotifier.IsDue(newStart, Today, 2, lastReminderSentFor: oldStart));
    }

    // --- Sweep (fake email + InMemory DB) --------------------------------------

    private class FakeEmailSender(string? throwFor = null) : IEmailSender
    {
        public readonly List<(string to, string subject, string html, string text)> Sent = [];

        public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody,
            CancellationToken ct = default)
        {
            if (throwFor == toEmail)
                throw new InvalidOperationException($"Simulated failure for {toEmail}");
            Sent.Add((toEmail, subject, htmlBody, textBody));
            return Task.CompletedTask;
        }
    }

    private static (ReminderNotifier notifier, string dbName, FakeEmailSender fake) Create(
        bool smtpConfigured = true, string? throwFor = null)
    {
        var dbName = $"reminder_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var fake = new FakeEmailSender(throwFor);
        var smtp = Options.Create(new SmtpOptions { Host = smtpConfigured ? "smtp.example.com" : "" });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NOTIFY_REMINDERS"] = "true",
                ["PUBLIC_BASE_URL"] = "https://thosedays.example.com",
            })
            .Build();

        var notifier = new ReminderNotifier(scopeFactory, fake, smtp, config,
            NullLogger<ReminderNotifier>.Instance);
        return (notifier, dbName, fake);
    }

    private static AppDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static User SeedUser(AppDbContext db, int? predictedDaysOut, bool optIn = true,
        bool active = true, int lead = 2, DateTime? lastSent = null, string email = "u@example.com")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "h",
            IsActive = active,
            NotifyPeriodReminder = optIn,
            ReminderLeadDays = lead,
            LastReminderSentFor = lastSent,
        };
        db.Users.Add(user);
        if (predictedDaysOut is not null)
            db.Predictions.Add(new Prediction
            {
                UserId = user.Id,
                PredictedStart = Day(predictedDaysOut.Value),
                PredictedDuration = 5,
                Confidence = 0.8f,
            });
        db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task Sweep_DueUser_SendsOnce()
    {
        var (notifier, dbName, fake) = Create();
        using (var db = NewDb(dbName)) SeedUser(db, predictedDaysOut: 1);

        var sent = await notifier.RunSweepAsync(Today, CancellationToken.None);

        Assert.Equal(1, sent);
        Assert.Single(fake.Sent);
        Assert.Contains("tomorrow", fake.Sent[0].subject);
    }

    [Fact]
    public async Task Sweep_IsIdempotent_SecondRunSendsNothing()
    {
        var (notifier, dbName, fake) = Create();
        using (var db = NewDb(dbName)) SeedUser(db, predictedDaysOut: 1);

        await notifier.RunSweepAsync(Today, CancellationToken.None);
        var secondRun = await notifier.RunSweepAsync(Today, CancellationToken.None);

        Assert.Equal(0, secondRun);
        Assert.Single(fake.Sent); // still just the one from the first run

        // The dedupe marker was persisted.
        using var check = NewDb(dbName);
        var u = await check.Users.FirstAsync();
        Assert.Equal(Day(1).Date, u.LastReminderSentFor!.Value.Date);
    }

    [Fact]
    public async Task Sweep_OptedOut_Inactive_OutOfWindow_AllSkipped()
    {
        var (notifier, dbName, fake) = Create();
        using (var db = NewDb(dbName))
        {
            SeedUser(db, predictedDaysOut: 1, optIn: false, email: "optout@example.com");
            SeedUser(db, predictedDaysOut: 1, active: false, email: "inactive@example.com");
            SeedUser(db, predictedDaysOut: 10, email: "faraway@example.com");
            SeedUser(db, predictedDaysOut: null, email: "noprediction@example.com");
        }

        var sent = await notifier.RunSweepAsync(Today, CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Empty(fake.Sent);
    }

    [Fact]
    public async Task Sweep_SmtpNotConfigured_NoOp()
    {
        var (notifier, dbName, fake) = Create(smtpConfigured: false);
        using (var db = NewDb(dbName)) SeedUser(db, predictedDaysOut: 1);

        var sent = await notifier.RunSweepAsync(Today, CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Empty(fake.Sent);
    }

    [Fact]
    public async Task Sweep_OneFailureDoesNotBlockOthers_AndFailedUserRetries()
    {
        var (notifier, dbName, fake) = Create(throwFor: "bad@example.com");
        using (var db = NewDb(dbName))
        {
            SeedUser(db, predictedDaysOut: 1, email: "bad@example.com");
            SeedUser(db, predictedDaysOut: 1, email: "good@example.com");
        }

        var sent = await notifier.RunSweepAsync(Today, CancellationToken.None);

        Assert.Equal(1, sent);
        Assert.Single(fake.Sent);
        Assert.Equal("good@example.com", fake.Sent[0].to);

        // The failed user's marker was NOT advanced → eligible again next sweep.
        using var check = NewDb(dbName);
        var bad = await check.Users.FirstAsync(u => u.Email == "bad@example.com");
        Assert.Null(bad.LastReminderSentFor);
    }

    [Fact]
    public async Task Sweep_RespectsPerUserLeadDays()
    {
        var (notifier, dbName, fake) = Create();
        using (var db = NewDb(dbName))
        {
            // 5 days out: due only for the user whose lead is >= 5.
            SeedUser(db, predictedDaysOut: 5, lead: 2, email: "shortlead@example.com");
            SeedUser(db, predictedDaysOut: 5, lead: 6, email: "longlead@example.com");
        }

        await notifier.RunSweepAsync(Today, CancellationToken.None);

        Assert.Single(fake.Sent);
        Assert.Equal("longlead@example.com", fake.Sent[0].to);
    }

    // --- Email content ---------------------------------------------------------

    [Fact]
    public void BuildReminderEmail_ContainsDate_LeadAndUnsubscribe()
    {
        var (subject, html, text) = ReminderNotifier.BuildReminderEmail(
            new DateTime(2026, 6, 15), 2, "https://app.example", "https://app.example/api/unsubscribe?token=x&kind=reminder");

        Assert.Contains("in 2 days", subject);
        Assert.Contains("15 Jun 2026", html);
        Assert.Contains("kind=reminder", html);
        Assert.Contains("kind=reminder", text);
        Assert.Contains("Unsubscribe", html);
    }
}
