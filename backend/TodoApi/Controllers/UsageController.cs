using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsageController(AppDbContext db) : ControllerBase
{
    // Neon free-tier storage cap per project = 0.5 GB. Surfaced so the UI can show how
    // close the database is to the point where you'd start paying.
    const long NeonFreeBytes = 512L * 1024 * 1024;

    [HttpGet]
    public IActionResult Get()
    {
        long dbBytes = 0;
        string provider;
        if (db.Database.IsNpgsql())
        {
            provider = "Neon Postgres";
            dbBytes = db.Database
                .SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"")
                .AsEnumerable().FirstOrDefault();
        }
        else if (db.Database.IsSqlite())
        {
            provider = "SQLite";
            var f = TodoApi.DataPaths.Db;
            if (System.IO.File.Exists(f)) dbBytes = new FileInfo(f).Length;
        }
        else
        {
            provider = db.Database.ProviderName ?? "unknown";
        }

        var (uploadBytes, uploadCount) = TodoApi.Usage.DirectorySize(TodoApi.DataPaths.Uploads);

        return Ok(new
        {
            db = new { provider, usedBytes = dbBytes, limitBytes = NeonFreeBytes },
            uploads = new { usedBytes = uploadBytes, fileCount = uploadCount },
            generatedAt = DateTime.UtcNow,
        });
    }
}
