using Api.DTOs;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/user/{userId}")]
public class PredictionsController(ICycleService cycleService) : ControllerBase
{
    [HttpPost("predict")]
    public async Task<ActionResult<List<PredictionResponse>>> GeneratePredictions(Guid userId, [FromQuery] int cycles = 15)
    {
        var predictions = await cycleService.GeneratePredictionsAsync(userId, cycles);
        var responses = predictions.Select(p => new PredictionResponse
        {
            Id = p.Id,
            PredictedStart = p.PredictedStart,
            PredictedDuration = p.PredictedDuration,
            Confidence = p.Confidence
        }).ToList();

        return Ok(responses);
    }

    [HttpGet("predictions")]
    public async Task<ActionResult<List<PredictionResponse>>> GetPredictions(Guid userId)
    {
        var predictions = await cycleService.GetPredictionsAsync(userId);
        return Ok(predictions.Select(p => new PredictionResponse
        {
            Id = p.Id,
            PredictedStart = p.PredictedStart,
            PredictedDuration = p.PredictedDuration,
            Confidence = p.Confidence
        }).ToList());
    }

    [HttpGet("stats")]
    public async Task<ActionResult<StatsResponse>> GetStats(Guid userId)
    {
        var (avgLength, avgInterval, totalPeriods) = await cycleService.GetStatsAsync(userId);

        var response = new StatsResponse
        {
            AverageCycleLength = avgLength,
            AverageInterval = avgInterval,
            TotalCycles = totalPeriods
        };

        return Ok(response);
    }

    [HttpPost("recalculate")]
    public async Task<ActionResult<RecalcResponse>> Recalculate(Guid userId, [FromBody] RecalcRequest request)
    {
        var days = new List<DateTime>();
        foreach (var d in request.Days)
        {
            if (DateTime.TryParse(d, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                days.Add(parsed);
            }
        }

        var (cycleLength, periodDuration, cycles, forecast) =
            await cycleService.RecalculateAsync(userId, days, request.CycleLength, request.PeriodDuration);

        return Ok(new RecalcResponse
        {
            CycleLength = cycleLength,
            PeriodDuration = periodDuration,
            Cycles = cycles.Select(c => new CycleResponse
            {
                Id = c.Id,
                StartDate = c.StartDate,
                DurationDays = c.DurationDays,
                CreatedAt = c.CreatedAt,
                Corrected = c.Corrected,
                Auto = c.Auto
            }).ToList(),
            Forecast = forecast.Select(p => new PredictionResponse
            {
                Id = p.Id,
                PredictedStart = p.PredictedStart,
                PredictedDuration = p.PredictedDuration,
                Confidence = p.Confidence
            }).ToList()
        });
    }

    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile(Guid userId, [FromQuery] string? today = null)
    {
        var localToday = DateTime.TryParse(today, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : DateTime.UtcNow;

        var converted = await cycleService.ReconcileAsync(userId, localToday);
        return Ok(new { converted });
    }

    [HttpPost("toggle")]
    public async Task<IActionResult> TogglePredictions(Guid userId)
    {
        return Ok(new { message = "Predictions toggled", enabled = true });
    }
}
