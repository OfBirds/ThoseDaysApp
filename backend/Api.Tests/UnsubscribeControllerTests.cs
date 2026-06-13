using Api.Controllers;
using Api.Data;
using Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests;

/// <summary>Tests for kind-scoped unsubscribe.</summary>
public class UnsubscribeControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly UnsubscribeController _controller;
    private readonly User _user;

    public UnsubscribeControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"unsub_{Guid.NewGuid()}").Options;
        _db = new AppDbContext(options);
        _user = new User
        {
            Email = "u@e.com",
            PasswordHash = "h",
            NotifyReleases = true,
            NotifyPeriodReminder = true,
        };
        _db.Users.Add(_user);
        _db.SaveChanges();
        _controller = new UnsubscribeController(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Reminder_ClearsOnlyReminderOptIn()
    {
        await _controller.Get(_user.UnsubscribeToken, "reminder", CancellationToken.None);

        var u = await _db.Users.FindAsync(_user.Id);
        Assert.False(u!.NotifyPeriodReminder);
        Assert.True(u.NotifyReleases); // release emails untouched
    }

    [Fact]
    public async Task Release_ClearsOnlyReleaseOptIn()
    {
        await _controller.Get(_user.UnsubscribeToken, "release", CancellationToken.None);

        var u = await _db.Users.FindAsync(_user.Id);
        Assert.False(u!.NotifyReleases);
        Assert.True(u.NotifyPeriodReminder); // reminders untouched
    }

    [Fact]
    public async Task NoKind_DefaultsToRelease()
    {
        await _controller.Get(_user.UnsubscribeToken, null, CancellationToken.None);

        var u = await _db.Users.FindAsync(_user.Id);
        Assert.False(u!.NotifyReleases);
        Assert.True(u.NotifyPeriodReminder);
    }

    [Fact]
    public async Task UnknownToken_NotFound()
    {
        var result = await _controller.Get(Guid.NewGuid(), "reminder", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task EmptyToken_BadRequest()
    {
        var result = await _controller.Get(Guid.Empty, null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }
}
