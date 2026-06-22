namespace TodoApi.Models;

/// <summary>
/// Scheduling/manifest metadata for a <see cref="TodoItem"/> (1:1, optional).
/// Kept in a sibling table so the hot TodoItem read/write/log path stays untouched.
/// List- and structure-valued fields are stored as JSON strings, mirroring the
/// convention used by <see cref="FilterBookmark"/>; they are parsed at the edges
/// (ManifestService / SchedulerService), never as EF owned types.
/// </summary>
public class TaskManifest
{
    public int Id { get; set; }

    /// <summary>FK to the owning TodoItem (unique — 1:1).</summary>
    public int TodoId { get; set; }

    /// <summary>Stable identifier used to reference this task from other tasks
    /// (dependencies / muze_bezet_s). Underscores, no spaces. Unique.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Raw effort token, e.g. "2h" / "15m". Parsed to minutes by the engine. Required field in the manifest.</summary>
    public string Odhad { get; set; } = string.Empty;

    /// <summary>Earliest start gate. Inherits the global UtcDateTimeConverter.</summary>
    public DateTime? MuzuZacit { get; set; }

    /// <summary>Soft target (recommendation only). Inherits the global UtcDateTimeConverter.</summary>
    public DateTime? Deadline { get; set; }

    /// <summary>HARD constraint: only schedulable inside working hours.</summary>
    public bool JenVPraci { get; set; }

    /// <summary>JSON string[] of slugs this task depends on (may be empty []).</summary>
    public string Dependencies { get; set; } = "[]";

    /// <summary>JSON string[] of preferred day-windows (rano/dopo/odpo/vecer). SOFT.</summary>
    public string Kdy { get; set; } = "[]";

    /// <summary>JSON string[] of slugs allowed to share a time window with this task.</summary>
    public string MuzeBezetS { get; set; } = "[]";

    /// <summary>JSON object { "kdo": "...", "reakce": "rychle|normalne|pomalu" } or "" .</summary>
    public string CekaNaCloveka { get; set; } = string.Empty;

    /// <summary>JSON object describing a fixed, immovable appointment, or "".
    /// Either { "datetime": "2026-06-03 10:00-11:00" } or, for periodic, { "time": "09:00-09:15" }.</summary>
    public string PevnyCas { get; set; } = string.Empty;

    /// <summary>Recurrence rhythm token, e.g. "denne" / "tydne:po,st,pa" / "mesicne:15" /
    /// "mesicne:prvni-streda" / "kvartalne" / "interval:14d". Empty = one-off.</summary>
    public string Periodicita { get; set; } = string.Empty;

    /// <summary>Phase-2 shape: learned attention split for concurrent work,
    /// JSON object { "slug": 0.5, ... }. Defined now, written later.</summary>
    public string AttentionSplit { get; set; } = string.Empty;
}
