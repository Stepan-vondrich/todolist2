using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsageController(AppDbContext db) : ControllerBase
{
    // Free-tier caps surfaced so the UI can show how close each is to paid billing.
    const long NeonFreeBytes = 512L * 1024 * 1024;   // Neon 0.5 GB / project
    const long GhcrFreeBytes = 500L * 1024 * 1024;   // GitHub Packages 500 MB storage

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    [HttpGet]
    public async Task<IActionResult> Get()
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
        var ghcr = await GhcrUsageAsync();

        return Ok(new
        {
            db = new { provider, usedBytes = dbBytes, limitBytes = NeonFreeBytes },
            uploads = new { usedBytes = uploadBytes, fileCount = uploadCount },
            ghcr,  // null when the registry can't be queried (no token / offline)
            generatedAt = DateTime.UtcNow,
        });
    }

    // Compressed image size in GHCR (config + layers), read from the registry. Best-effort:
    // needs GHCR_IMAGE (e.g. "owner/repo") and GHCR_TOKEN (read:packages PAT). Returns null
    // on any failure so the panel just shows a dash rather than breaking the whole response.
    static async Task<object?> GhcrUsageAsync()
    {
        var image = Environment.GetEnvironmentVariable("GHCR_IMAGE");
        var token = Environment.GetEnvironmentVariable("GHCR_TOKEN");
        if (string.IsNullOrWhiteSpace(image) || string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var user = image.Split('/')[0];
            var tokReq = new HttpRequestMessage(HttpMethod.Get, $"https://ghcr.io/token?scope=repository:{image}:pull");
            tokReq.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{token}")));
            var tokRes = await Http.SendAsync(tokReq);
            if (!tokRes.IsSuccessStatusCode) return null;
            using var tokDoc = JsonDocument.Parse(await tokRes.Content.ReadAsStringAsync());
            var pull = tokDoc.RootElement.GetProperty("token").GetString();

            var manReq = new HttpRequestMessage(HttpMethod.Get, $"https://ghcr.io/v2/{image}/manifests/latest");
            manReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pull);
            manReq.Headers.Accept.ParseAdd("application/vnd.oci.image.manifest.v1+json");
            manReq.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.v2+json");
            var manRes = await Http.SendAsync(manReq);
            if (!manRes.IsSuccessStatusCode) return null;

            var bytes = TodoApi.Usage.ManifestTotalBytes(await manRes.Content.ReadAsStringAsync());
            return bytes > 0 ? new { usedBytes = bytes, limitBytes = GhcrFreeBytes } : null;
        }
        catch { return null; }
    }
}
