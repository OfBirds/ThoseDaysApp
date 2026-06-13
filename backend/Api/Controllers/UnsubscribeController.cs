using Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Route("api/unsubscribe")]
public class UnsubscribeController(AppDbContext db) : ControllerBase
{
    // GET because it's clicked straight from an email link. Idempotent and
    // token-scoped, so re-clicks (or a client prefetch) just re-confirm.
    // `kind` scopes which emails to stop: "reminder" → period reminders,
    // "release" (or anything else / absent) → release emails (back-compat with
    // links already sent before reminders existed).
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid token, [FromQuery] string? kind, CancellationToken ct)
    {
        if (token == Guid.Empty)
            return BadRequest("Missing unsubscribe token.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.UnsubscribeToken == token, ct);
        if (user is null)
            return NotFound("Unknown unsubscribe link.");

        if (string.Equals(kind, "reminder", StringComparison.OrdinalIgnoreCase))
        {
            if (user.NotifyPeriodReminder)
            {
                user.NotifyPeriodReminder = false;
                await db.SaveChangesAsync(ct);
            }
            return Content("You've been unsubscribed from ThoseDays period reminders.", "text/plain");
        }

        if (user.NotifyReleases)
        {
            user.NotifyReleases = false;
            await db.SaveChangesAsync(ct);
        }

        return Content("You've been unsubscribed from ThoseDays release emails.", "text/plain");
    }
}
