using Api.DTOs;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/user/{userId}/cycles")]
public class CyclesController(ICycleService cycleService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CycleResponse>>> GetCycles(Guid userId)
    {
        var cycles = await cycleService.GetUserCyclesAsync(userId);
        var responses = cycles.Select(c => new CycleResponse
        {
            Id = c.Id,
            StartDate = c.StartDate,
            DurationDays = c.DurationDays,
            CreatedAt = c.CreatedAt,
            Corrected = c.Corrected,
            Auto = c.Auto
        }).ToList();

        return Ok(responses);
    }

    [HttpPost]
    public async Task<ActionResult<CycleResponse>> AddCycle(Guid userId, [FromBody] CreateCycleRequest request)
    {
        var cycle = await cycleService.AddCycleAsync(userId, request.StartDate, request.DurationDays);
        var response = new CycleResponse
        {
            Id = cycle.Id,
            StartDate = cycle.StartDate,
            DurationDays = cycle.DurationDays,
            CreatedAt = cycle.CreatedAt,
            Corrected = cycle.Corrected,
            Auto = cycle.Auto
        };

        return CreatedAtAction(nameof(GetCycles), new { userId }, response);
    }

    [HttpPut("{cycleId}")]
    public async Task<ActionResult<CycleResponse>> UpdateCycle(Guid userId, Guid cycleId, [FromBody] UpdateCycleRequest request)
    {
        var cycle = await cycleService.UpdateCycleAsync(userId, cycleId, request.StartDate, request.DurationDays);
        if (cycle == null)
            return NotFound();

        var response = new CycleResponse
        {
            Id = cycle.Id,
            StartDate = cycle.StartDate,
            DurationDays = cycle.DurationDays,
            CreatedAt = cycle.CreatedAt,
            Corrected = cycle.Corrected,
            Auto = cycle.Auto
        };

        return Ok(response);
    }

    [HttpDelete("{cycleId}")]
    public async Task<IActionResult> DeleteCycle(Guid userId, Guid cycleId)
    {
        var success = await cycleService.DeleteCycleAsync(userId, cycleId);
        if (!success)
            return NotFound();

        return NoContent();
    }
}
