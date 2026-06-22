using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/task-logs")]
public class TaskLogsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetForTodo([FromQuery] int todoId)
    {
        var logs = await db.TaskLogs
            .Where(l => l.TodoId == todoId)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();
        return Ok(logs);
    }
}
