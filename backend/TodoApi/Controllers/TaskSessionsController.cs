using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/task-sessions")]
public class TaskSessionsController(AppDbContext db) : ControllerBase
{
    // GET /api/task-sessions/active  → list of todoIds currently being tracked
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var ids = await db.TaskSessions
            .Where(s => s.EndedAt == null)
            .Select(s => s.TodoId)
            .ToListAsync();
        return Ok(ids);
    }

    // GET /api/task-sessions?todoId=X  → full history for one todo
    [HttpGet]
    public async Task<IActionResult> GetForTodo([FromQuery] int todoId)
    {
        var sessions = await db.TaskSessions
            .Where(s => s.TodoId == todoId)
            .OrderBy(s => s.StartedAt)
            .ToListAsync();
        return Ok(sessions);
    }

    // POST /api/task-sessions/start/{todoId}
    [HttpPost("start/{todoId}")]
    public async Task<IActionResult> Start(int todoId)
    {
        var already = await db.TaskSessions
            .AnyAsync(s => s.TodoId == todoId && s.EndedAt == null);
        if (already) return Conflict("Already active.");

        var activeCount = await db.TaskSessions.CountAsync(s => s.EndedAt == null);

        var session = new TaskSession
        {
            TodoId           = todoId,
            StartedAt        = DateTime.UtcNow,
            ActiveCountAtStart = activeCount,
        };
        db.TaskSessions.Add(session);
        await db.SaveChangesAsync();
        return Ok(session);
    }

    public record EndSessionDto(string? Comment);
    public record UpdateSessionDto(DateTime StartedAt, DateTime? EndedAt, string? Comment);

    // PUT /api/task-sessions/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSessionDto dto)
    {
        var session = await db.TaskSessions.FindAsync(id);
        if (session == null) return NotFound();
        session.StartedAt = dto.StartedAt;
        session.EndedAt   = dto.EndedAt;
        session.Comment   = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        await db.SaveChangesAsync();
        return Ok(session);
    }

    // DELETE /api/task-sessions/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var session = await db.TaskSessions.FindAsync(id);
        if (session == null) return NotFound();
        // Remove related overlaps too
        var overlaps = db.TaskOverlaps.Where(o => o.SessionId == id || o.OverlappingSessionId == id);
        db.TaskOverlaps.RemoveRange(overlaps);
        db.TaskSessions.Remove(session);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/task-sessions/end/{todoId}
    [HttpPost("end/{todoId}")]
    public async Task<IActionResult> End(int todoId, [FromBody] EndSessionDto? dto = null)
    {
        var session = await db.TaskSessions
            .Where(s => s.TodoId == todoId && s.EndedAt == null)
            .FirstOrDefaultAsync();
        if (session == null) return NotFound("No active session.");

        var endTime = DateTime.UtcNow;
        session.EndedAt = endTime;
        if (!string.IsNullOrWhiteSpace(dto?.Comment))
            session.Comment = dto.Comment.Trim();
        await db.SaveChangesAsync();

        // Find all sessions that overlapped with this one
        var overlapping = await db.TaskSessions
            .Where(s => s.Id != session.Id
                     && s.StartedAt < endTime
                     && (s.EndedAt == null || s.EndedAt > session.StartedAt))
            .ToListAsync();

        foreach (var other in overlapping)
        {
            var overlapStart = other.StartedAt > session.StartedAt ? other.StartedAt : session.StartedAt;
            var overlapEnd   = (other.EndedAt ?? endTime) < endTime ? (other.EndedAt ?? endTime) : endTime;
            if (overlapEnd <= overlapStart) continue;

            db.TaskOverlaps.Add(new TaskOverlap
            {
                SessionId            = session.Id,
                OverlappingSessionId = other.Id,
                TodoId               = session.TodoId,
                OverlappingTodoId    = other.TodoId,
                OverlapStart         = overlapStart,
                OverlapEnd           = overlapEnd,
                OverlapMinutes       = (overlapEnd - overlapStart).TotalMinutes,
            });
        }

        await db.SaveChangesAsync();
        return Ok(session);
    }
}
