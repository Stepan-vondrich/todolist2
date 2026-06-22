using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Services;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlanController(AppDbContext db, ManifestService manifest, SchedulerService scheduler) : ControllerBase
{
    /// <summary>
    /// Forward-simulate the whole horizon and return the predicted plan.
    /// <paramref name="horizon"/> (e.g. "3m", "6w") overrides the saved setting for this call.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? horizon = null)
    {
        var manifests = await db.TaskManifests.ToListAsync();
        var todoIds = manifests.Select(m => m.TodoId).ToList();
        var todos = await db.Todos.Where(t => todoIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

        var tasks = manifests
            .Where(m => todos.ContainsKey(m.TodoId))
            .Select(m => PlanParsing.ToPlanTask(todos[m.TodoId], m))
            .ToList();

        var settings = PlanParsing.FromNastaveni((await manifest.LoadSettingsAsync()).Nastaveni);
        if (!string.IsNullOrWhiteSpace(horizon)) settings.HorizonRaw = horizon;

        var result = scheduler.Simulate(new SchedulerInput
        {
            Tasks = tasks,
            Settings = settings,
            Now = DateTime.Now, // wall-clock; the engine treats this as the user's local time
        });

        return Ok(result);
    }
}
