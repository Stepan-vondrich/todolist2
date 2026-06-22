using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExportController(AppDbContext db) : ControllerBase
{
    public record PasswordDto(string Password, bool IncludeFiles = true);

    record ExportData(
        int Version,
        DateTime ExportedAt,
        List<TodoItem> Todos,
        List<Comment> Comments,
        List<TaskSession>? Sessions = null,
        List<TaskOverlap>? Overlaps = null,
        List<TaskLog>? Logs = null
    );

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Encrypted backup export ──────────────────────────────────────────────

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] PasswordDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Password is required.");

        var todos    = await db.Todos.OrderBy(t => t.CreatedAt).ToListAsync();
        var comments = await db.Comments.OrderBy(c => c.CreatedAt).ToListAsync();
        var sessions = await db.TaskSessions.OrderBy(s => s.StartedAt).ToListAsync();
        var overlaps = await db.TaskOverlaps.OrderBy(o => o.Id).ToListAsync();
        var logs     = await db.TaskLogs.OrderBy(l => l.Id).ToListAsync();

        using var zipMs = new MemoryStream();
        using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var json = JsonSerializer.Serialize(
                new ExportData(2, DateTime.UtcNow, todos, comments, sessions, overlaps, logs), JsonOpts);
            var jsonEntry = zip.CreateEntry("data.json", CompressionLevel.Fastest);
            using (var jw = new StreamWriter(jsonEntry.Open()))
                await jw.WriteAsync(json);

            if (req.IncludeFiles)
            {
                var uploadsDir = TodoApi.DataPaths.Uploads;
                if (Directory.Exists(uploadsDir))
                {
                    foreach (var filePath in Directory.GetFiles(uploadsDir))
                    {
                        var entry = zip.CreateEntry($"files/{Path.GetFileName(filePath)}", CompressionLevel.Fastest);
                        using var src = System.IO.File.OpenRead(filePath);
                        using var dst = entry.Open();
                        await src.CopyToAsync(dst);
                    }
                }
            }
        }

        var encrypted = Encrypt(zipMs.ToArray(), req.Password);
        return File(encrypted, "application/octet-stream", "todolist.backup");
    }

    // ── Encrypted backup import ──────────────────────────────────────────────

    [HttpPost("import")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Import([FromForm] string password, IFormFile file, [FromForm] string mode = "replace")
    {
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest("Password is required.");

        using var fileMs = new MemoryStream();
        await file.CopyToAsync(fileMs);

        byte[] zipBytes;
        try { zipBytes = Decrypt(fileMs.ToArray(), password); }
        catch { return BadRequest("Wrong password or corrupted file."); }

        ExportData data;
        try
        {
            using var zipMs = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(zipMs, ZipArchiveMode.Read);

            var dataEntry = zip.GetEntry("data.json") ?? throw new Exception("Missing data.json");
            using var reader = new StreamReader(dataEntry.Open());
            data = JsonSerializer.Deserialize<ExportData>(await reader.ReadToEndAsync(), JsonOpts)!;

            var uploadsDir = TodoApi.DataPaths.Uploads;
            Directory.CreateDirectory(uploadsDir);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("files/") && e.Name.Length > 0))
            {
                var destPath = Path.Combine(uploadsDir, entry.Name);
                if (mode != "replace" && System.IO.File.Exists(destPath)) continue;
                await using var src = entry.Open();
                await using var dst = System.IO.File.Create(destPath);
                await src.CopyToAsync(dst);
            }
        }
        catch (Exception ex) { return BadRequest($"Invalid backup: {ex.Message}"); }

        var backupSessions = data.Sessions ?? [];
        var backupOverlaps = data.Overlaps ?? [];
        var backupLogs     = data.Logs ?? [];

        int addedTodos, addedComments, addedSessions, addedOverlaps, addedLogs;

        if (mode == "merge" || mode == "addonly")
        {
            var backupTodoIds    = data.Todos.Select(t => t.Id).ToHashSet();
            var backupCommentIds = data.Comments.Select(c => c.Id).ToHashSet();
            var backupSessionIds = backupSessions.Select(s => s.Id).ToHashSet();
            var backupOverlapIds = backupOverlaps.Select(o => o.Id).ToHashSet();
            var backupLogIds     = backupLogs.Select(l => l.Id).ToHashSet();

            var currentTodos    = await db.Todos.ToListAsync();
            var currentComments = await db.Comments.ToListAsync();
            var currentSessions = await db.TaskSessions.ToListAsync();
            var currentOverlaps = await db.TaskOverlaps.ToListAsync();
            var currentTodoIds    = currentTodos.Select(t => t.Id).ToHashSet();
            var currentCommentIds = currentComments.Select(c => c.Id).ToHashSet();
            var currentSessionIds = currentSessions.Select(s => s.Id).ToHashSet();
            var currentOverlapIds = currentOverlaps.Select(o => o.Id).ToHashSet();
            var currentLogIds     = (await db.TaskLogs.Select(l => l.Id).ToListAsync()).ToHashSet();

            if (mode == "merge")
            {
                db.TaskOverlaps.RemoveRange(currentOverlaps.Where(o => !backupOverlapIds.Contains(o.Id)));
                await db.SaveChangesAsync();
                db.TaskSessions.RemoveRange(currentSessions.Where(s => !backupSessionIds.Contains(s.Id)));
                await db.SaveChangesAsync();
                db.Comments.RemoveRange(currentComments.Where(c => !backupCommentIds.Contains(c.Id)));
                await db.SaveChangesAsync();
                db.Todos.RemoveRange(currentTodos.Where(t => !backupTodoIds.Contains(t.Id)));
                await db.SaveChangesAsync(); // cascades to TaskLogs for removed todos
            }

            var todosToAdd    = data.Todos.Where(t => !currentTodoIds.Contains(t.Id)).ToList();
            var commentsToAdd = data.Comments.Where(c => !currentCommentIds.Contains(c.Id)).ToList();
            var sessionsToAdd = backupSessions.Where(s => !currentSessionIds.Contains(s.Id)).ToList();
            var overlapsToAdd = backupOverlaps.Where(o => !currentOverlapIds.Contains(o.Id)).ToList();

            foreach (var t in todosToAdd)    db.Todos.Add(t);
            await db.SaveChangesAsync();

            // After todos are in DB, add logs whose TodoId now exists
            var validTodoIds  = (await db.Todos.Select(t => t.Id).ToListAsync()).ToHashSet();
            // Refresh log IDs (merge may have cascade-deleted some)
            currentLogIds = (await db.TaskLogs.Select(l => l.Id).ToListAsync()).ToHashSet();
            var logsToAdd = backupLogs
                .Where(l => !currentLogIds.Contains(l.Id) && validTodoIds.Contains(l.TodoId))
                .ToList();

            foreach (var c in commentsToAdd) db.Comments.Add(c);
            await db.SaveChangesAsync();
            foreach (var s in sessionsToAdd) db.TaskSessions.Add(s);
            await db.SaveChangesAsync();
            foreach (var o in overlapsToAdd) db.TaskOverlaps.Add(o);
            await db.SaveChangesAsync();
            foreach (var l in logsToAdd) db.TaskLogs.Add(l);
            await db.SaveChangesAsync();

            addedTodos    = todosToAdd.Count;
            addedComments = commentsToAdd.Count;
            addedSessions = sessionsToAdd.Count;
            addedOverlaps = overlapsToAdd.Count;
            addedLogs     = logsToAdd.Count;
        }
        else // replace
        {
            db.TaskOverlaps.RemoveRange(db.TaskOverlaps);
            await db.SaveChangesAsync();
            db.TaskSessions.RemoveRange(db.TaskSessions);
            await db.SaveChangesAsync();
            db.Comments.RemoveRange(db.Comments);
            await db.SaveChangesAsync();
            db.Todos.RemoveRange(db.Todos);
            await db.SaveChangesAsync(); // cascades to TaskLogs

            foreach (var t in data.Todos)     db.Todos.Add(t);
            await db.SaveChangesAsync();
            foreach (var c in data.Comments)  db.Comments.Add(c);
            await db.SaveChangesAsync();
            foreach (var s in backupSessions) db.TaskSessions.Add(s);
            await db.SaveChangesAsync();
            foreach (var o in backupOverlaps) db.TaskOverlaps.Add(o);
            await db.SaveChangesAsync();
            foreach (var l in backupLogs)     db.TaskLogs.Add(l);
            await db.SaveChangesAsync();

            addedTodos    = data.Todos.Count;
            addedComments = data.Comments.Count;
            addedSessions = backupSessions.Count;
            addedOverlaps = backupOverlaps.Count;
            addedLogs     = backupLogs.Count;
        }

        return Ok(new { todos = addedTodos, comments = addedComments, sessions = addedSessions, overlaps = addedOverlaps, logs = addedLogs, mode });
    }

    // ── Time tracking CSV export (no password) ───────────────────────────────

    [HttpGet("export-time")]
    public async Task<IActionResult> ExportTime()
    {
        var todos    = await db.Todos.ToDictionaryAsync(t => t.Id, t => t.Title);
        var sessions = await db.TaskSessions.OrderBy(s => s.StartedAt).ToListAsync();
        var overlaps = await db.TaskOverlaps.OrderBy(o => o.Id).ToListAsync();

        const char Sep = ';';
        static string Csv(string s) => s.Contains(';') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

        var sessionsCsv = new StringBuilder();
        sessionsCsv.AppendLine("id;todoId;todoTitle;startedAt;endedAt;durationMinutes;activeCountAtStart;comment");
        foreach (var s in sessions)
        {
            var title    = todos.GetValueOrDefault(s.TodoId, "");
            var end      = s.EndedAt ?? DateTime.UtcNow;
            var duration = (end - s.StartedAt).TotalMinutes;
            sessionsCsv.AppendLine($"{s.Id}{Sep}{s.TodoId}{Sep}{Csv(title)}{Sep}{s.StartedAt:o}{Sep}{(s.EndedAt.HasValue ? s.EndedAt.Value.ToString("o") : "")}{Sep}{duration:F1}{Sep}{s.ActiveCountAtStart}{Sep}{Csv(s.Comment ?? "")}");
        }

        var overlapsCsv = new StringBuilder();
        overlapsCsv.AppendLine("id;sessionId;todoId;todoTitle;overlappingSessionId;overlappingTodoId;overlappingTodoTitle;overlapStart;overlapEnd;overlapMinutes");
        foreach (var o in overlaps)
        {
            var title1 = todos.GetValueOrDefault(o.TodoId, "");
            var title2 = todos.GetValueOrDefault(o.OverlappingTodoId, "");
            overlapsCsv.AppendLine($"{o.Id}{Sep}{o.SessionId}{Sep}{o.TodoId}{Sep}{Csv(title1)}{Sep}{o.OverlappingSessionId}{Sep}{o.OverlappingTodoId}{Sep}{Csv(title2)}{Sep}{o.OverlapStart:o}{Sep}{o.OverlapEnd:o}{Sep}{o.OverlapMinutes:F1}");
        }

        using var zipMs = new MemoryStream();
        using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e1 = zip.CreateEntry("sessions.csv", CompressionLevel.Fastest);
            using (var w = new StreamWriter(e1.Open(), Encoding.UTF8)) await w.WriteAsync(sessionsCsv.ToString());

            var e2 = zip.CreateEntry("overlaps.csv", CompressionLevel.Fastest);
            using (var w = new StreamWriter(e2.Open(), Encoding.UTF8)) await w.WriteAsync(overlapsCsv.ToString());
        }

        return File(zipMs.ToArray(), "application/zip", $"todolist-time-{DateTime.UtcNow:yyyyMMdd}.zip");
    }

    // ── Time tracking ZIP import (addonly, no password) ─────────────────────

    [HttpPost("import-time")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportTime(IFormFile? file)
    {
        if (file == null) return BadRequest("Soubor nebyl nahrán.");

        using var zipMs = new MemoryStream();
        await file.CopyToAsync(zipMs);
        zipMs.Position = 0;

        string sessionsCsv = "", overlapsCsv = "";
        try
        {
            using var zip = new ZipArchive(zipMs, ZipArchiveMode.Read);
            var sEntry = zip.GetEntry("sessions.csv");
            var oEntry = zip.GetEntry("overlaps.csv");
            if (sEntry == null && oEntry == null)
                return BadRequest("ZIP musí obsahovat sessions.csv nebo overlaps.csv.");
            if (sEntry != null) using (var r = new StreamReader(sEntry.Open())) sessionsCsv = await r.ReadToEndAsync();
            if (oEntry != null) using (var r = new StreamReader(oEntry.Open())) overlapsCsv = await r.ReadToEndAsync();
        }
        catch (Exception ex) { return BadRequest($"Neplatný ZIP: {ex.Message}"); }

        var existingSessionIds = (await db.TaskSessions.Select(s => s.Id).ToListAsync()).ToHashSet();
        var existingOverlapIds = (await db.TaskOverlaps.Select(o => o.Id).ToListAsync()).ToHashSet();

        int addedSessions = 0, addedOverlaps = 0;

        // Parse sessions.csv
        if (!string.IsNullOrWhiteSpace(sessionsCsv))
        {
            var lines = sessionsCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
            if (lines.Count > 1)
            {
                var hdrs = ParseCsvRow(lines[0]).Select(h => h.Trim().ToLower()).ToList();
                int C(string n) => hdrs.IndexOf(n);
                int iId = C("id"), iTodo = C("todoid"), iStart = C("startedat"), iEnd = C("endedat"), iActive = C("activecountatstart"), iComment = C("comment");

                foreach (var line in lines.Skip(1))
                {
                    var cols = ParseCsvRow(line);
                    string G(int i) => i >= 0 && i < cols.Count ? cols[i].Trim() : "";
                    if (!int.TryParse(G(iId), out var id)) continue;
                    if (existingSessionIds.Contains(id)) continue;
                    if (!int.TryParse(G(iTodo), out var todoId)) continue;
                    if (!DateTime.TryParse(G(iStart), out var start)) continue;
                    DateTime? end = DateTime.TryParse(G(iEnd), out var e) ? e : null;
                    int.TryParse(G(iActive), out var active);
                    var comment = iComment >= 0 ? G(iComment) : null;

                    db.TaskSessions.Add(new TaskSession { Id = id, TodoId = todoId, StartedAt = start, EndedAt = end, ActiveCountAtStart = active, Comment = string.IsNullOrEmpty(comment) ? null : comment });
                    existingSessionIds.Add(id);
                    addedSessions++;
                }
                await db.SaveChangesAsync();
            }
        }

        // Parse overlaps.csv
        if (!string.IsNullOrWhiteSpace(overlapsCsv))
        {
            var lines = overlapsCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
            if (lines.Count > 1)
            {
                var hdrs = ParseCsvRow(lines[0]).Select(h => h.Trim().ToLower()).ToList();
                int C(string n) => hdrs.IndexOf(n);
                int iId = C("id"), iSes = C("sessionid"), iTodo = C("todoid"), iOSes = C("overlappingsessionid"),
                    iOTodo = C("overlappingtodoid"), iStart = C("overlapstart"), iEnd = C("overlapend"), iMin = C("overlapminutes");

                foreach (var line in lines.Skip(1))
                {
                    var cols = ParseCsvRow(line);
                    string G(int i) => i >= 0 && i < cols.Count ? cols[i].Trim() : "";
                    if (!int.TryParse(G(iId), out var id)) continue;
                    if (existingOverlapIds.Contains(id)) continue;
                    if (!int.TryParse(G(iSes), out var sesId)) continue;
                    if (!int.TryParse(G(iTodo), out var todoId)) continue;
                    if (!int.TryParse(G(iOSes), out var oSesId)) continue;
                    if (!int.TryParse(G(iOTodo), out var oTodoId)) continue;
                    if (!DateTime.TryParse(G(iStart), out var start)) continue;
                    if (!DateTime.TryParse(G(iEnd), out var end)) continue;
                    double.TryParse(G(iMin), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mins);

                    db.TaskOverlaps.Add(new TaskOverlap { Id = id, SessionId = sesId, TodoId = todoId, OverlappingSessionId = oSesId, OverlappingTodoId = oTodoId, OverlapStart = start, OverlapEnd = end, OverlapMinutes = mins });
                    existingOverlapIds.Add(id);
                    addedOverlaps++;
                }
                await db.SaveChangesAsync();
            }
        }

        return Ok(new { addedSessions, addedOverlaps });
    }

    // ── CSV import (addonly, no password) ────────────────────────────────────

    [HttpPost("import-csv")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportCsv(IFormFile? file)
    {
        if (file == null) return BadRequest("Soubor nebyl nahrán.");
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count < 2) return BadRequest("CSV musí mít hlavičku a alespoň jeden řádek.");

        var headers = ParseCsvRow(lines[0]).Select(h => h.Trim().ToLower()).ToList();
        int Col(string name) => headers.IndexOf(name.ToLower());
        int expectedCols = headers.Count;

        int iTitle  = Col("title");  if (iTitle < 0) return BadRequest("CSV musí mít sloupec 'title'.");
        int iParent = Col("parent"), iStatus = Col("status"), iPriority = Col("priority"),
            iRelated = Col("related"), iDetail = Col("detailrelated"), iDate = Col("duedate");

        var existingByTitle = new Dictionary<string, int>();
        foreach (var t in await db.Todos.ToListAsync()) existingByTitle[t.Title] = t.Id;
        var addedByTitle = new Dictionary<string, int>();

        int added = 0;
        foreach (var line in lines.Skip(1))
        {
            var cols = ParseCsvRow(line);
            if (cols.Count > expectedCols)
            {
                int extra = cols.Count - expectedCols;
                var merged = string.Join(";", cols.Take(extra + 1));
                cols = new List<string> { merged }.Concat(cols.Skip(extra + 1)).ToList();
            }
            string Get(int i) => i >= 0 && i < cols.Count ? cols[i].Trim() : "";

            var title = Get(iTitle);
            if (string.IsNullOrWhiteSpace(title)) continue;

            int? parentId = null;
            var parentTitle = Get(iParent);
            if (!string.IsNullOrWhiteSpace(parentTitle))
            {
                if (existingByTitle.TryGetValue(parentTitle, out var pid)) parentId = pid;
                else if (addedByTitle.TryGetValue(parentTitle, out var pid2)) parentId = pid2;
            }

            DateTime? dueDate = null;
            var dateStr = Get(iDate);
            if (!string.IsNullOrWhiteSpace(dateStr) && DateTime.TryParse(dateStr, out var d))
                dueDate = DateTime.SpecifyKind(d, DateTimeKind.Utc);

            var todo = new TodoItem
            {
                Title         = title,
                ParentId      = parentId,
                Status        = Get(iStatus),
                Priority      = Get(iPriority),
                Related       = Get(iRelated),
                DetailRelated = Get(iDetail),
                DueDate       = dueDate,
                CreatedAt     = DateTime.UtcNow,
            };
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            existingByTitle[todo.Title] = todo.Id;
            addedByTitle[todo.Title]    = todo.Id;
            added++;
        }

        return Ok(new { added });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static List<string> ParseCsvRow(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ';' && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }

    static byte[] Encrypt(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var iv   = RandomNumberGenerator.GetBytes(16);
        var key  = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
        using var aes = Aes.Create();
        aes.Key = key; aes.IV = iv;
        using var ms = new MemoryStream();
        ms.Write(salt); ms.Write(iv);
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            cs.Write(plaintext);
        return ms.ToArray();
    }

    static byte[] Decrypt(byte[] data, string password)
    {
        var salt = data[..16];
        var iv   = data[16..32];
        var key  = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
        using var aes = Aes.Create();
        aes.Key = key; aes.IV = iv;
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(new MemoryStream(data[32..]), aes.CreateDecryptor(), CryptoStreamMode.Read))
            cs.CopyTo(ms);
        return ms.ToArray();
    }
}
