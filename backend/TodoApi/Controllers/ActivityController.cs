using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;

namespace TodoApi.Controllers;

/// <summary>
/// Returns the ids of todos that had activity of the requested kinds within a
/// date range — used by the "activity date" filter in the UI. Activity kinds:
///   created   → the todo's CreatedAt falls in range
///   modified  → it has a TaskLog (other than "create") in range
///   commented → it has a Comment created in range
/// Range bounds are inclusive at day granularity (UTC). An empty range means
/// "no date restriction" (every todo with the requested activity kind matches).
/// </summary>
[ApiController]
[Route("api/activity")]
public class ActivityController(AppDbContext db) : ControllerBase
{
    static readonly string[] AllKinds = { "created", "modified", "commented" };

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? types)
    {
        DateOnly? fromDate = ParseDate(from);
        DateOnly? toDate = ParseDate(to);

        var kinds = string.IsNullOrWhiteSpace(types)
            ? AllKinds
            : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var wantCreated   = kinds.Contains("created");
        var wantModified  = kinds.Contains("modified");
        var wantCommented = kinds.Contains("commented");

        var matched = new HashSet<int>();

        if (wantCreated)
        {
            var todos = await db.Todos.Select(t => new { t.Id, t.CreatedAt }).ToListAsync();
            foreach (var t in todos)
                if (InRange(t.CreatedAt, fromDate, toDate)) matched.Add(t.Id);
        }

        if (wantModified)
        {
            // Any log that isn't the initial "create" counts as a modification.
            var logs = await db.TaskLogs
                .Where(l => l.EventType != "create")
                .Select(l => new { l.TodoId, l.Timestamp })
                .ToListAsync();
            foreach (var l in logs)
                if (InRange(l.Timestamp, fromDate, toDate)) matched.Add(l.TodoId);
        }

        if (wantCommented)
        {
            var comments = await db.Comments.Select(c => new { c.TodoId, c.CreatedAt }).ToListAsync();
            foreach (var c in comments)
                if (InRange(c.CreatedAt, fromDate, toDate)) matched.Add(c.TodoId);
        }

        return Ok(matched);
    }

    static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // Inclusive day-level comparison against the LOCAL calendar date. Timestamps are
    // stored as UTC, but the date the user picks in the UI is their local date — and
    // this desktop app runs in the user's timezone — so convert to local before
    // comparing, or a task created tonight (UTC = yesterday) would miss "today".
    static bool InRange(DateTime ts, DateOnly? from, DateOnly? to)
    {
        var local = ts.Kind == DateTimeKind.Utc ? ts.ToLocalTime()
                  : ts.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(ts, DateTimeKind.Utc).ToLocalTime()
                  : ts.ToLocalTime();
        var day = DateOnly.FromDateTime(local);
        if (from.HasValue && day < from.Value) return false;
        if (to.HasValue && day > to.Value) return false;
        return true;
    }
}
