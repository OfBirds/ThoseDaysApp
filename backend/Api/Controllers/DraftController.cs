using System.Globalization;
using System.Text.Json;
using Api.Data;
using Api.DTOs;
using Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

/// <summary>
/// Server copy of the calendar's painted-but-not-recalculated day-set, so an unsaved
/// draft survives refreshes and other devices. Inert by design: it feeds no statistics
/// or forecasts — Recalculate commits the days and clears it (see CycleService).
/// </summary>
[ApiController]
[Route("api/user/{userId}/draft")]
public class DraftController(AppDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DraftResponse>> Get(Guid userId)
    {
        var draft = await context.CalendarDrafts.FirstOrDefaultAsync(d => d.UserId == userId);
        if (draft == null)
            return NoContent();

        // A draft older than the newest committed cycle predates history it does not
        // contain. The calendar seeds from a draft in preference to the actuals, so
        // serving a stale one hides committed days and Recalculate then deletes them.
        // Unsaved paint is worth less than committed history: don't serve it.
        //
        // Read-only on purpose. Deleting the row here would race a concurrent PUT (read,
        // then unconditional delete by key) and throw away a draft that had just been
        // saved. It also does not need deleting: every path that writes Cycles either
        // merges into the draft or clears it (CycleService), so this check is a backstop,
        // and the next PUT overwrites the row anyway.
        var newestCycle = await context.Cycles
            .Where(c => c.UserId == userId)
            .MaxAsync(c => (DateTime?)c.CreatedAt);

        if (newestCycle > draft.UpdatedAt)
            return NoContent();

        var days = JsonSerializer.Deserialize<List<string>>(draft.DaysJson) ?? [];
        return Ok(new DraftResponse { Days = days, UpdatedAt = draft.UpdatedAt });
    }

    [HttpPut]
    public async Task<IActionResult> Put(Guid userId, [FromBody] SaveDraftRequest request)
    {
        var days = (request.Days ?? [])
            .Where(d => DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var draft = await context.CalendarDrafts.FirstOrDefaultAsync(d => d.UserId == userId);
        if (draft == null)
        {
            draft = new CalendarDraft { UserId = userId };
            context.CalendarDrafts.Add(draft);
        }
        draft.DaysJson = JsonSerializer.Serialize(days);
        draft.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return NoContent();
    }
}
