using Api.Config;
using Api.Controllers;
using Api.Data;
using Api.DTOs;
using Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests;

/// <summary>
/// GET /draft is the backstop that stops a draft older than the committed history from being
/// served — the calendar seeds from a draft in preference to the actuals, so serving a stale
/// one hides committed days and Recalculate then offers to delete them.
/// </summary>
public class DraftControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly DraftController _controller;
    private readonly Guid _userId = Guid.NewGuid();

    public DraftControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"thosedays_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
        _controller = new DraftController(_db);

        _db.Users.Add(new User { Id = _userId, Email = "draft@example.com", PasswordHash = "hash" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private void SeedDraft(DateTime updatedAt) =>
        _db.CalendarDrafts.Add(new CalendarDraft
        {
            UserId = _userId,
            DaysJson = """["2026-08-12"]""",
            UpdatedAt = updatedAt
        });

    private void SeedCycle(DateTime createdAt) =>
        _db.Cycles.Add(new Cycle
        {
            UserId = _userId,
            StartDate = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc),
            DurationDays = 4,
            CreatedAt = createdAt
        });

    [Fact]
    public async Task Get_NoDraft_ReturnsNoContent()
    {
        var result = await _controller.Get(_userId);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task Get_DraftNewerThanCycles_IsServed()
    {
        SeedCycle(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        SeedDraft(new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        var result = await _controller.Get(_userId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<DraftResponse>(ok.Value);
        Assert.Equal(["2026-08-12"], body.Days);
    }

    [Fact]
    public async Task Get_DraftOlderThanNewestCycle_IsNotServed()
    {
        SeedDraft(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        SeedCycle(new DateTime(2026, 8, 29, 0, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        var result = await _controller.Get(_userId);

        Assert.IsType<NoContentResult>(result.Result);
    }

    /// <summary>
    /// Read-only on purpose: deleting here would race a concurrent PUT and throw away a draft
    /// that had just been saved. Not serving it is enough.
    /// </summary>
    [Fact]
    public async Task Get_StaleDraft_IsNotDeleted()
    {
        SeedDraft(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        SeedCycle(new DateTime(2026, 8, 29, 0, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        await _controller.Get(_userId);

        Assert.Single(_db.CalendarDrafts.Where(d => d.UserId == _userId));
    }

    [Fact]
    public async Task Get_DraftWithNoCyclesAtAll_IsServed()
    {
        SeedDraft(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        var result = await _controller.Get(_userId);

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
