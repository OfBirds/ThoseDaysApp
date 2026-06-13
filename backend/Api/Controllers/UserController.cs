using Api.DTOs;
using Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Route("api/user/{userId}")]
public class UserController(AppDbContext db) : ControllerBase
{
    [HttpGet("prefs")]
    public async Task<ActionResult<UserPrefsResponse>> GetPrefs(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return NotFound();

        return Ok(new UserPrefsResponse { NotifyReleases = user.NotifyReleases });
    }

    [HttpPut("prefs")]
    public async Task<ActionResult<UserPrefsResponse>> UpdatePrefs(
        Guid userId, [FromBody] UpdateUserPrefsRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return NotFound();

        user.NotifyReleases = request.NotifyReleases;
        await db.SaveChangesAsync(ct);

        return Ok(new UserPrefsResponse { NotifyReleases = user.NotifyReleases });
    }
}
