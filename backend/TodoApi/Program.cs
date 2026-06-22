using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Services;

[assembly: InternalsVisibleTo("TodoApi.Tests")]

// Pin both the content root and the database to the exe's directory so the app
// works correctly regardless of the working directory at launch time.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// User data (db + uploads) lives under DataPaths.Root, redirectable via DATA_DIR
// (a mounted persistent volume in the container). Ensure it exists before use.
Directory.CreateDirectory(TodoApi.DataPaths.Root);
// Pick the database provider: Azure SQL in the container (persistent, set via the
// AZURE_SQL_CONNECTION env var), SQLite for the local published exe (file next to the app).
var azureSqlConn = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION");
builder.Services.AddDbContext<AppDbContext>(opt =>
{
    if (!string.IsNullOrWhiteSpace(azureSqlConn))
        opt.UseSqlServer(azureSqlConn);
    else
        opt.UseSqlite($"Data Source={TodoApi.DataPaths.Db}");
});

builder.Services.AddControllers();

builder.Services.AddScoped<TodoApi.Services.ManifestService>();
builder.Services.AddScoped<TodoApi.Services.SchedulerService>();

builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p =>
        p.WithOrigins("http://localhost:6173")
         .AllowAnyHeader()
         .AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // SQLite carries the migration history; for Azure SQL (and the in-memory test db)
    // build the schema directly from the current model.
    if (db.Database.IsSqlite())
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();

    // One-time backfill: index searchable text from file attachments uploaded
    // before this feature existed. After the first run they're no longer null,
    // so this loop finds nothing and is effectively free on later startups.
    var uploadsRoot = TodoApi.DataPaths.Uploads;
    var pending = db.CommentAttachments
        .Where(a => a.Type == "file" && a.ExtractedText == null)
        .ToList();
    foreach (var att in pending)
    {
        try
        {
            var file = Path.Combine(uploadsRoot, Path.GetFileName(att.Path));
            if (!File.Exists(file)) continue;
            var text = AttachmentTextExtractor.Extract(File.ReadAllBytes(file), att.Path);
            // Mark as processed either way (empty string) so we don't retry every boot.
            att.ExtractedText = text ?? "";
        }
        catch { /* skip unreadable file, retry next boot */ }
    }
    if (pending.Count > 0) db.SaveChanges();
}

app.UseCors();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Never cache index.html — always fetch fresh so new deploys are picked up immediately
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    }
});

var uploadsPath = TodoApi.DataPaths.Uploads;
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
});

app.MapControllers();
app.MapFallbackToFile("index.html");

// Listen URL: honour ASPNETCORE_URLS if set (e.g. for local testing on another port),
// otherwise default to :6001 as before.
var listenUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
var primaryUrl = string.IsNullOrEmpty(listenUrls) ? "http://localhost:6001" : listenUrls.Split(';')[0];

// Auto-open browser when running as published exe on Windows (not in dev mode,
// and not inside a Linux container where there is no browser / shell handler).
if (!app.Environment.IsDevelopment() && OperatingSystem.IsWindows())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try { Process.Start(new ProcessStartInfo(primaryUrl) { UseShellExecute = true }); }
        catch { /* no browser available — ignore */ }
    });
}

if (string.IsNullOrEmpty(listenUrls))
    app.Run("http://localhost:6001");
else
    app.Run();

public partial class Program { }
