namespace Api.DTOs;

/// <summary>
/// The portable export/backup document (see docs/data-export-import.md). One JSON
/// file that can be re-imported as a reviewed patch. Never contains secrets
/// (password hash, unsubscribe token); predictions are derived, so not exported.
/// </summary>
public class ExportDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Kind { get; set; } = "export";   // "export" | "backup"
    public string Scope { get; set; } = "full";     // "full" | "patch"
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string? AppVersion { get; set; }
    public ExportRange? Range { get; set; }
    public int CycleCount { get; set; }
    public ExportAccount? Account { get; set; }
    public List<ExportCycle> Cycles { get; set; } = [];
}

/// <summary>The covered window: earliest cycle start to the latest cycle's last bleeding day.</summary>
public class ExportRange
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
}

/// <summary>Non-secret account metadata — informational on import, never applied.</summary>
public class ExportAccount
{
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool NotifyReleases { get; set; }
    public bool NotifyPeriodReminder { get; set; }
}

public class ExportCycle
{
    public string StartDate { get; set; } = "";   // yyyy-MM-dd
    public int DurationDays { get; set; }
    public bool Corrected { get; set; }
    public bool Auto { get; set; }
    public string? PredictedStart { get; set; }    // yyyy-MM-dd or null
    public DateTime CreatedAt { get; set; }
}

/// <summary>Summary returned after a successful import patch.</summary>
public class ImportResultResponse
{
    public int Added { get; set; }
    public int Removed { get; set; }
    public ExportRange? Range { get; set; }
}
