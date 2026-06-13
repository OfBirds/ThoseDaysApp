using System.Globalization;
using System.Text;
using System.Text.Json;
using Api.DTOs;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/user/{userId}")]
public class DataController(ICycleService cycleService, IConfiguration config) : ControllerBase
{
    private const int MaxImportCycles = 5000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Download all data (or the most recent N cycles) as a JSON file.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid userId, [FromQuery] int? cycles)
    {
        var appVersion = config["APP_VERSION"];
        var doc = await cycleService.BuildExportAsync(userId, cycles, "export", appVersion);
        var fileName = CycleService.ExportFileName(doc);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(doc, Json));
        return File(bytes, "application/json", fileName);
    }

    /// <summary>
    /// Apply an import as a reviewed patch (called by "Save this history permanently",
    /// not on file select). Validates first; on any problem nothing is written.
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportResultResponse>> Import(Guid userId, [FromBody] ExportDocument doc)
    {
        if (doc is null)
            return BadRequest(new { error = "Missing import document." });

        if (doc.SchemaVersion != 1)
            return BadRequest(new { error = $"Unsupported file version ({doc.SchemaVersion}). This app reads version 1." });

        if (doc.Cycles.Count == 0)
            return BadRequest(new { error = "The file has no cycles to import." });

        if (doc.Cycles.Count > MaxImportCycles)
            return BadRequest(new { error = $"Too many cycles ({doc.Cycles.Count}); the limit is {MaxImportCycles}." });

        for (var i = 0; i < doc.Cycles.Count; i++)
        {
            var c = doc.Cycles[i];
            if (!DateTime.TryParse(c.StartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return BadRequest(new { error = $"Cycle {i + 1} has an invalid start date ('{c.StartDate}')." });
            if (c.DurationDays < 1 || c.DurationDays > 30)
                return BadRequest(new { error = $"Cycle {i + 1} has an invalid duration ({c.DurationDays} days)." });
            if (!string.IsNullOrWhiteSpace(c.PredictedStart) &&
                !DateTime.TryParse(c.PredictedStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return BadRequest(new { error = $"Cycle {i + 1} has an invalid predicted-start date." });
        }

        var result = await cycleService.PatchCyclesAsync(userId, doc.Cycles);
        return Ok(result);
    }
}
