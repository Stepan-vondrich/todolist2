namespace TodoApi.Services;

// ── Engine input (plain POCOs; no EF, so the algorithm is unit-testable) ────────

public class CekaNaClovekaInfo
{
    public string Kdo { get; set; } = string.Empty;
    public string Reakce { get; set; } = "normalne";
}

/// <summary>A fixed, immovable appointment. Either a concrete date+range, or (for
/// periodic appointments) a time-of-day range whose dates come from periodicita.</summary>
public class PevnyCasInfo
{
    public DateTime? Start { get; set; }   // concrete appointment start (wall-clock)
    public DateTime? End { get; set; }     // concrete appointment end
    public TimeSpan? TimeFrom { get; set; } // periodic: time-of-day only
    public TimeSpan? TimeTo { get; set; }
}

public class PlanTask
{
    public int TodoId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int EstimateMinutes { get; set; }
    public List<string> Dependencies { get; set; } = new();
    public DateTime? MuzuZacit { get; set; }
    public DateTime? Deadline { get; set; }
    public bool JenVPraci { get; set; }
    public List<string> Kdy { get; set; } = new();
    public List<string> MuzeBezetS { get; set; } = new();
    public CekaNaClovekaInfo? CekaNaCloveka { get; set; }
    public PevnyCasInfo? PevnyCas { get; set; }
    public string Periodicita { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    // Set by periodicity expansion (engine-internal).
    public DateTime? OccurrenceDate { get; set; }
    /// <summary>A later, not-yet-active occurrence: lives on the timeline only, excluded from now/blocked.</summary>
    public bool IsFutureOccurrence { get; set; }

    public bool Done => IsCompleted || Status == "done";
}

public class PlanSettings
{
    /// <summary>Work intervals (wall-clock time-of-day) per weekday. Missing weekday = non-working.</summary>
    public Dictionary<DayOfWeek, List<(TimeSpan from, TimeSpan to)>> WorkHours { get; set; } = new();
    /// <summary>Named day-windows rano/dopo/odpo/vecer → time-of-day range.</summary>
    public Dictionary<string, (TimeSpan from, TimeSpan to)> Windows { get; set; } = new();
    /// <summary>rychle/normalne/pomalu → expected wait for a person to respond.</summary>
    public Dictionary<string, TimeSpan> ReakceLidi { get; set; } = new();
    public string HorizonRaw { get; set; } = "3m";
}

public class SchedulerInput
{
    public List<PlanTask> Tasks { get; set; } = new();
    public PlanSettings Settings { get; set; } = new();
    /// <summary>Injected "now" (wall-clock) so the simulation is deterministic in tests.</summary>
    public DateTime Now { get; set; }
}

// ── Engine output ──────────────────────────────────────────────────────────────

public class BlockedByInfo
{
    public string Kind { get; set; } = string.Empty; // "task" | "person" | "start"
    public string Ref { get; set; } = string.Empty;  // slug or person name
    public string Reason { get; set; } = string.Empty;
}

public class PlanNode
{
    public int TodoId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime PredictedStart { get; set; }
    public DateTime PredictedFinish { get; set; }
    public DateTime? Deadline { get; set; }
    public double SlackMinutes { get; set; }
    public string State { get; set; } = string.Empty; // now|next|blocked|at_risk|future|done
    public BlockedByInfo? BlockedBy { get; set; }
    public int DownstreamImpactCount { get; set; }
    public bool SoftWindowMissed { get; set; }
    public List<string> SharesWindowWith { get; set; } = new();
    public bool IsOccurrence { get; set; }
    public DateTime? OccurrenceDate { get; set; }
}

public class PlanAlert
{
    public string Type { get; set; } = string.Empty; // dependency_cycle | periodic_stuck | bottleneck
    public string Message { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public int? TodoId { get; set; }
    public int DownstreamImpact { get; set; }
}

public class PlanResult
{
    public DateTime ComputedAt { get; set; }
    public string Horizon { get; set; } = string.Empty;
    public PlanNode? Now { get; set; }
    public List<PlanNode> Next { get; set; } = new();
    public List<PlanNode> Blocked { get; set; } = new();
    public List<PlanNode> AtRisk { get; set; } = new();
    public List<PlanAlert> Alerts { get; set; } = new();
    public List<PlanNode> Timeline { get; set; } = new();
}
