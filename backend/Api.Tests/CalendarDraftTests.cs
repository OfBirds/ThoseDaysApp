using Api.Config;
using Api.Data;
using Api.Models;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Tests;

/// <summary>Server-side calendar draft: inert storage, cleared when Recalculate commits.</summary>
public class CalendarDraftTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CycleService _svc;
    private readonly Guid _userId = Guid.NewGuid();

    public CalendarDraftTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"thosedays_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
        _svc = new CycleService(_db, Options.Create(new RecalcConfig
        {
            Weights = [3, 2, 1],
            TailWeight = 1,
            DefaultCycleLength = 28,
            DefaultPeriodDuration = 5,
            ForecastCount = 15
        }));

        _db.Users.Add(new User { Id = _userId, Email = "draft@example.com", PasswordHash = "hash" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RecalculateAsync_ClearsSavedDraft()
    {
        _db.CalendarDrafts.Add(new CalendarDraft
        {
            UserId = _userId,
            DaysJson = """["2026-01-01"]""",
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _svc.RecalculateAsync(_userId, [new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)], null, null);

        Assert.Empty(_db.CalendarDrafts.Where(d => d.UserId == _userId));
    }

    [Fact]
    public async Task RecalculateAsync_NoDraft_StillSucceeds()
    {
        var (cycleLength, _, cycles, _) = await _svc.RecalculateAsync(
            _userId, [new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)], null, null);

        Assert.Single(cycles);
        Assert.True(cycleLength > 0);
    }
}
