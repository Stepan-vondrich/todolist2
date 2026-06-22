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

var dbPath = Path.Combine(AppContext.BaseDirectory, "todos.db");
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

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
    if (db.Database.IsRelational())
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();

    // One-time backfill: index searchable text from file attachments uploaded
    // before this feature existed. After the first run they're no longer null,
    // so this loop finds nothing and is effectively free on later startups.
    var uploadsRoot = Path.Combine(AppContext.BaseDirectory, "uploads");
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

var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
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

// Auto-open browser when running as published exe (not in dev mode)
if (!app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
        Process.Start(new ProcessStartInfo(primaryUrl) { UseShellExecute = true }));
}

if (string.IsNullOrEmpty(listenUrls))
    app.Run("http://localhost:6001");
else
    app.Run();

public partial class Program { }
