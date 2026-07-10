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
// Pick the database provider, by env var, most-specific first:
//   POSTGRES_CONNECTION  -> PostgreSQL (e.g. Neon; used by the dev container)
//   AZURE_SQL_CONNECTION -> Azure SQL  (prod container, persistent)
//   neither              -> SQLite     (local published exe, file next to the app)
var pgConn = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION");
var azureSqlConn = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION");
builder.Services.AddDbContext<AppDbContext>(opt =>
{
    if (!string.IsNullOrWhiteSpace(pgConn))
        opt.UseNpgsql(TodoApi.PgConnection.Normalize(pgConn));
    else if (!string.IsNullOrWhiteSpace(azureSqlConn))
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
        .Where(a => a.Type == "file"
            && (a.ExtractedText == null
                || (a.PageTexts == null && a.Path.ToLower().EndsWith(".pdf"))))
        .ToList();
    foreach (var att in pending)
    {
        try
        {
            var file = Path.Combine(uploadsRoot, Path.GetFileName(att.Path));
            if (!File.Exists(file)) continue;
            var bytes = File.ReadAllBytes(file);
            if (att.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                // Read the PDF once; derive both per-page and flat text from it.
                var pages = AttachmentTextExtractor.ExtractPdfPages(bytes);
                if (pages is not null)
                {
                    att.PageTexts ??= System.Text.Json.JsonSerializer.Serialize(pages);
                    att.ExtractedText ??= AttachmentTextExtractor.FlattenPages(pages);
                }
                // Mark as processed even if extraction failed, so we don't retry every boot.
                att.ExtractedText ??= "";
            }
            else if (att.ExtractedText == null)
            {
                att.ExtractedText = AttachmentTextExtractor.Extract(bytes, att.Path) ?? "";
            }
        }
        catch { /* skip unreadable file, retry next boot */ }
    }
    if (pending.Count > 0) db.SaveChanges();
}

// Optional mutual-TLS gate — when CLIENT_CERT_SHA256 is set, the caller must present the pinned
// client certificate (forwarded by the Container Apps ingress in X-Forwarded-Client-Cert). No
// matching cert => 403, regardless of password. Unset => not enforced (safe rollout toggle).
var pinnedCert = Environment.GetEnvironmentVariable("CLIENT_CERT_SHA256");
if (!string.IsNullOrEmpty(pinnedCert))
{
    var certLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ClientCert");
    app.Use(async (ctx, next) =>
    {
        var xfcc = ctx.Request.Headers["X-Forwarded-Client-Cert"].ToString();
        if (TodoApi.ClientCertGate.IsAuthorized(xfcc, pinnedCert))
            await next();
        else
        {
            certLog.LogWarning("[cert] rejected {Path} — no matching client cert. XFCC='{Xfcc}'",
                ctx.Request.Path, xfcc.Length > 200 ? xfcc[..200] + "…" : xfcc);
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsync("Client certificate required.");
        }
    });
}

// Optional Basic Auth gate — protects the WHOLE app (static files, uploads, API) when
// APP_AUTH_USER + APP_AUTH_PASS are set (the Azure containers). Unset (local exe) = open.
var authUser = Environment.GetEnvironmentVariable("APP_AUTH_USER");
var authPass = Environment.GetEnvironmentVariable("APP_AUTH_PASS");
if (!string.IsNullOrEmpty(authUser) && !string.IsNullOrEmpty(authPass))
{
    app.Use(async (ctx, next) =>
    {
        if (TodoApi.BasicAuth.IsAuthorized(ctx.Request.Headers.Authorization.ToString(), authUser, authPass))
            await next();
        else
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"Todo\", charset=\"UTF-8\"";
        }
    });
}

// Optional single-user gate for Azure Container Apps built-in auth (EasyAuth): when ALLOWED_EMAIL
// is set, only that logged-in Google account may pass — everyone else gets 403, regardless of the
// OAuth consent screen's publish/testing status. EasyAuth already authenticated the caller and
// forwards their identity in X-MS-CLIENT-PRINCIPAL(-NAME); those headers can't be client-spoofed.
var allowedEmail = Environment.GetEnvironmentVariable("ALLOWED_EMAIL");
if (!string.IsNullOrEmpty(allowedEmail))
{
    var emailLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EmailGate");
    app.Use(async (ctx, next) =>
    {
        var name      = ctx.Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"].ToString();
        var principal = ctx.Request.Headers["X-MS-CLIENT-PRINCIPAL"].ToString();
        if (TodoApi.EmailGate.IsAuthorized(name, principal, allowedEmail))
            await next();
        else
        {
            emailLog.LogWarning("[email] rejected {Path} — principal-name='{Name}' not the allowed account", ctx.Request.Path, name);
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsync("Access restricted to the owner's account.");
        }
    });
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
