using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExportController(AppDbContext db, ILogger<ExportController> logger) : ControllerBase
{
    public record PasswordDto(string Password, bool IncludeFiles = true);

    record ExportData(
        int Version,
        DateTime ExportedAt,
        List<TodoItem> Todos,
        List<Comment> Comments,
        List<TaskSession>? Sessions = null,
        List<TaskOverlap>? Overlaps = null,
        List<TaskLog>? Logs = null,
        List<FilterBookmark>? Bookmarks = null
    );

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Encrypted backup export ──────────────────────────────────────────────

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] PasswordDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Password is required.");

        var todos    = await db.Todos.OrderBy(t => t.CreatedAt).ToListAsync();
        // Include attachments so their DB rows (path, filename, extracted text) travel in the
        // backup — otherwise the files land in uploads/ on import but no comment references them,
        // and images/PDFs render as empty comments.
        var comments = await db.Comments.Include(c => c.Attachments).OrderBy(c => c.CreatedAt).ToListAsync();
        var sessions = await db.TaskSessions.OrderBy(s => s.StartedAt).ToListAsync();
        var overlaps = await db.TaskOverlaps.OrderBy(o => o.Id).ToListAsync();
        var logs     = await db.TaskLogs.OrderBy(l => l.Id).ToListAsync();
        var bookmarks = await db.FilterBookmarks.OrderBy(b => b.Id).ToListAsync();

        // Build the zip on disk and encrypt it straight into the HTTP response — never hold the
        // whole (multi-hundred-MB) backup in memory. The old MemoryStream + Encrypt(ToArray())
        // path OOM'd the small container once attachments were large.
        var tempZip = Path.Combine(Path.GetTempPath(), $"todoexport_{Guid.NewGuid():N}.zip");
        try
        {
            await using (var zipFile = System.IO.File.Create(tempZip))
            using (var zip = new ZipArchive(zipFile, ZipArchiveMode.Create))
            {
                var jsonEntry = zip.CreateEntry("data.json", CompressionLevel.Fastest);
                await using (var js = jsonEntry.Open())
                    await JsonSerializer.SerializeAsync(js,
                        new ExportData(2, DateTime.UtcNow, todos, comments, sessions, overlaps, logs, bookmarks), JsonOpts);

                if (req.IncludeFiles)
                {
                    var uploadsDir = TodoApi.DataPaths.Uploads;
                    if (Directory.Exists(uploadsDir))
                    {
                        foreach (var filePath in Directory.GetFiles(uploadsDir))
                        {
                            var entry = zip.CreateEntry($"files/{Path.GetFileName(filePath)}", CompressionLevel.Fastest);
                            await using var src = System.IO.File.OpenRead(filePath);
                            await using var dst = entry.Open();
                            await src.CopyToAsync(dst);
                        }
                    }
                }
            }

            Response.ContentType = "application/octet-stream";
            Response.Headers.ContentDisposition = "attachment; filename=todolist.backup";
            await using (var zipRead = System.IO.File.OpenRead(tempZip))
                await BackupCrypto.EncryptToStreamAsync(zipRead, req.Password, Response.Body);
            return new EmptyResult();
        }
        finally { try { System.IO.File.Delete(tempZip); } catch { } }
    }

    // ── Encrypted backup import ──────────────────────────────────────────────

    [HttpPost("import")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> Import()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Parse the multipart body ourselves with MultipartReader instead of [FromForm]/IFormFile.
        // Model binding buffers the whole (multi-hundred-MB) upload to a temp file first and, on
        // any hiccup, fails opaquely with a 400 ValidationProblemDetails *before* our code runs —
        // no logs, no clue why. Reading the stream directly lets us decrypt the file part straight
        // to disk (low memory, single temp file) and log every step. The frontend sends the
        // password + mode fields *before* the file part so we hold the key when the file arrives.
        var contentType = Request.ContentType ?? "";
        if (!contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("[import] rejected: not multipart (content-type='{CT}')", contentType);
            return BadRequest("Expected multipart/form-data.");
        }
        var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(contentType).Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
            return BadRequest("Missing multipart boundary.");
        logger.LogInformation("[import] start: content-length={LenMB:F1} MB", (Request.ContentLength ?? 0) / 1024.0 / 1024.0);

        string? password = null;
        string mode = "replace";
        bool gotFile = false;
        var tempZip = Path.Combine(Path.GetTempPath(), $"todobackup_{Guid.NewGuid():N}.zip");
        ExportData data;
        try
        {
            var reader = new MultipartReader(boundary, Request.Body);
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync()) != null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd))
                    continue;
                var name   = HeaderUtilities.RemoveQuotes(cd.Name).Value;
                var isFile = !string.IsNullOrEmpty(HeaderUtilities.RemoveQuotes(cd.FileName).Value)
                          || !string.IsNullOrEmpty(HeaderUtilities.RemoveQuotes(cd.FileNameStar).Value);

                if (isFile)
                {
                    if (string.IsNullOrEmpty(password))
                    {
                        logger.LogWarning("[import] file part arrived before the password field");
                        return BadRequest("Password must be sent before the file.");
                    }
                    logger.LogInformation("[import] receiving file part, streaming decrypt to temp…");
                    try
                    {
                        await using var dec = System.IO.File.Create(tempZip);
                        await BackupCrypto.DecryptToStreamAsync(section.Body, password, dec);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("[import] decrypt failed after {Ms} ms: {Error}", sw.ElapsedMilliseconds, ex.Message);
                        return BadRequest("Wrong password or corrupted file.");
                    }
                    gotFile = true;
                    logger.LogInformation("[import] decrypted to temp: {ZipMB:F1} MB in {Ms} ms",
                        new FileInfo(tempZip).Length / 1024.0 / 1024.0, sw.ElapsedMilliseconds);
                }
                else
                {
                    using var sr = new StreamReader(section.Body, Encoding.UTF8);
                    var val = (await sr.ReadToEndAsync()).Trim();
                    if (name == "password") password = val;
                    else if (name == "mode" && !string.IsNullOrWhiteSpace(val)) mode = val;
                }
            }

            if (string.IsNullOrWhiteSpace(password)) return BadRequest("Password is required.");
            if (!gotFile) return BadRequest("No file provided.");

            try
            {
                using var zip = ZipFile.OpenRead(tempZip);

                var dataEntry = zip.GetEntry("data.json") ?? throw new Exception("Missing data.json");
                await using (var dataStream = dataEntry.Open())
                    data = (await JsonSerializer.DeserializeAsync<ExportData>(dataStream, JsonOpts))!;
                logger.LogInformation("[import] parsed data.json: todos={Todos}, comments={Comments}, sessions={Sessions}, overlaps={Overlaps}, logs={Logs}",
                    data.Todos.Count, data.Comments.Count, (data.Sessions ?? []).Count, (data.Overlaps ?? []).Count, (data.Logs ?? []).Count);

                var uploadsDir = TodoApi.DataPaths.Uploads;
                Directory.CreateDirectory(uploadsDir);
                int extracted = 0;
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("files/") && e.Name.Length > 0))
                {
                    var destPath = Path.Combine(uploadsDir, entry.Name);
                    if (mode != "replace" && System.IO.File.Exists(destPath)) continue;
                    await using var src = entry.Open();
                    await using var dst = System.IO.File.Create(destPath);
                    await src.CopyToAsync(dst);
                    extracted++;
                }
                logger.LogInformation("[import] extracted {Extracted} attachment file(s) in {Ms} ms total", extracted, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[import] unzip/parse failed after {Ms} ms", sw.ElapsedMilliseconds);
                return BadRequest($"Invalid backup: {ex.Message}");
            }
        }
        finally { try { System.IO.File.Delete(tempZip); } catch { } }

        var backupSessions   = data.Sessions ?? [];
        var backupOverlaps   = data.Overlaps ?? [];
        var backupLogs       = data.Logs ?? [];
        var backupBookmarks  = data.Bookmarks ?? [];

        int addedTodos, addedComments, addedSessions, addedOverlaps, addedLogs;
        int updatedTodos = 0;

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

            // Update todos that already exist (matched by Id) to the backup's version, so a task
            // that was renamed or moved under a different parent — but not deleted — syncs instead
            // of being silently skipped. (addonly/merge previously only added brand-new ids.) Run
            // after the adds so a re-parent onto a newly-added parent resolves.
            var backupTodoById = data.Todos.ToDictionary(t => t.Id);
            foreach (var existing in currentTodos)
            {
                if (!backupTodoById.TryGetValue(existing.Id, out var bt)) continue;
                existing.Title         = bt.Title;
                existing.ParentId      = bt.ParentId;
                existing.Status        = bt.Status;
                existing.Priority      = bt.Priority;
                existing.Related       = bt.Related;
                existing.DetailRelated = bt.DetailRelated;
                existing.DueDate       = bt.DueDate;
                existing.SortOrder     = bt.SortOrder;
                existing.IsCompleted   = bt.IsCompleted;
                existing.CreatedAt     = bt.CreatedAt;
                updatedTodos++;
            }
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

            // Merge comments that already exist (matched by Id): update the text and reconcile the
            // attachment set — add ones the backup has, drop ones it no longer has, refresh matched
            // fields — so edited text and added/removed attachments on still-existing comments sync
            // (not just brand-new comments). Files themselves ride in via the extraction loop above.
            var backupCommentById = data.Comments.ToDictionary(c => c.Id);
            var existingComments  = await db.Comments.Include(c => c.Attachments)
                .Where(c => currentCommentIds.Contains(c.Id)).ToListAsync();
            foreach (var existing in existingComments)
            {
                if (!backupCommentById.TryGetValue(existing.Id, out var bc)) continue;
                existing.Text      = bc.Text;
                existing.TodoId    = bc.TodoId;
                existing.CreatedAt = bc.CreatedAt;

                var backupAtt = (bc.Attachments ?? new List<TodoApi.Models.CommentAttachment>()).ToDictionary(a => a.Id);
                var currentAtt = existing.Attachments.ToDictionary(a => a.Id);
                foreach (var a in existing.Attachments.Where(a => !backupAtt.ContainsKey(a.Id)).ToList())
                    db.CommentAttachments.Remove(a);
                foreach (var ba in backupAtt.Values)
                {
                    if (currentAtt.TryGetValue(ba.Id, out var ca))
                    {
                        ca.Path = ba.Path; ca.FileName = ba.FileName; ca.Type = ba.Type;
                        ca.Preview = ba.Preview; ca.SortOrder = ba.SortOrder;
                        ca.ExtractedText = ba.ExtractedText; ca.PageTexts = ba.PageTexts;
                    }
                    else
                    {
                        db.CommentAttachments.Add(new TodoApi.Models.CommentAttachment
                        {
                            Id = ba.Id, CommentId = existing.Id, Path = ba.Path, FileName = ba.FileName,
                            Type = ba.Type, Preview = ba.Preview, SortOrder = ba.SortOrder,
                            ExtractedText = ba.ExtractedText, PageTexts = ba.PageTexts,
                        });
                    }
                }
            }
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

        // Restore filter bookmarks (standalone, no FKs, in every mode). replace/merge mirror the
        // backup (drop bookmarks it doesn't have); addonly keeps local-only ones. Matched ids are
        // updated, new ones added — same add/update/remove shape as todos.
        var currentBookmarks = await db.FilterBookmarks.ToListAsync();
        var backupBmById     = backupBookmarks.ToDictionary(b => b.Id);
        var currentBmIds     = currentBookmarks.Select(b => b.Id).ToHashSet();
        if (mode != "addonly")
            db.FilterBookmarks.RemoveRange(currentBookmarks.Where(b => !backupBmById.ContainsKey(b.Id)));
        foreach (var existing in currentBookmarks)
            if (backupBmById.TryGetValue(existing.Id, out var bb)) CopyBookmark(bb, existing);
        foreach (var b in backupBookmarks.Where(b => !currentBmIds.Contains(b.Id)))
            db.FilterBookmarks.Add(b);
        await db.SaveChangesAsync();

        // Rows were inserted with explicit PK values, which does NOT advance Postgres identity
        // sequences — so the next auto-generated Id collides with an already-imported row and
        // every new todo/comment/subtask fails ("Failed to add subtask"). SQLite tracks max
        // rowid so this only bites Postgres. Bump each sequence to its column's current max.
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("""
                DO $$
                DECLARE rec RECORD; maxid BIGINT;
                BEGIN
                  FOR rec IN
                    -- deptype 'a' = serial columns, 'i' = GENERATED AS IDENTITY (EF Core/Npgsql
                    -- default); we must cover both or identity sequences are silently skipped.
                    SELECT s.relname AS seq, t.relname AS tbl, a.attname AS col
                    FROM pg_class s
                    JOIN pg_depend d ON d.objid = s.oid AND d.deptype IN ('a','i') AND d.refobjsubid > 0
                    JOIN pg_class t ON t.oid = d.refobjid
                    JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = d.refobjsubid
                    JOIN pg_namespace n ON n.oid = s.relnamespace
                    WHERE s.relkind = 'S' AND n.nspname = 'public'
                  LOOP
                    EXECUTE format('SELECT COALESCE(MAX(%I),0) FROM %I', rec.col, rec.tbl) INTO maxid;
                    -- setval's first arg is regclass: pass the quoted identifier as a literal so
                    -- mixed-case names (e.g. "CommentAttachments_Id_seq") resolve instead of being
                    -- folded to lowercase (which would raise "relation does not exist").
                    IF maxid < 1 THEN
                      EXECUTE format('SELECT setval(%L, 1, false)', quote_ident(rec.seq));
                    ELSE
                      EXECUTE format('SELECT setval(%L, %s, true)', quote_ident(rec.seq), maxid);
                    END IF;
                  END LOOP;
                END $$;
                """);
            logger.LogInformation("[import] resynced Postgres identity sequences");
        }

        // Drop comments orphaned by a since-deleted todo (their TodoId no longer exists) — older
        // deletions didn't cascade comments, so these could linger. Removing them frees their
        // attachment rows, so the files fall out of the referenced set and get pruned below.
        var liveTodoIds = (await db.Todos.Select(t => t.Id).ToListAsync()).ToHashSet();
        var orphanComments = await db.Comments.Where(c => !liveTodoIds.Contains(c.TodoId)).ToListAsync();
        if (orphanComments.Count > 0)
        {
            db.Comments.RemoveRange(orphanComments); // cascades to CommentAttachments
            await db.SaveChangesAsync();
            logger.LogInformation("[import] removed {N} comment(s) orphaned by deleted todos", orphanComments.Count);
        }

        // Enforce "no orphaned files": every file in uploads must be referenced by an attachment
        // (its Path or Preview). Delete any upload file nothing points to after this import — e.g.
        // attachments dropped during a merge, or extracted files that ended up unreferenced.
        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in await db.CommentAttachments.AsNoTracking().ToListAsync())
        {
            if (!string.IsNullOrEmpty(a.Path))    referencedFiles.Add(Path.GetFileName(a.Path));
            if (!string.IsNullOrEmpty(a.Preview)) referencedFiles.Add(Path.GetFileName(a.Preview));
        }
        var pruneDir = TodoApi.DataPaths.Uploads;
        int prunedFiles = 0;
        if (Directory.Exists(pruneDir))
        {
            foreach (var f in Directory.GetFiles(pruneDir))
            {
                if (referencedFiles.Contains(Path.GetFileName(f))) continue;
                try { System.IO.File.Delete(f); prunedFiles++; } catch { }
            }
        }
        if (prunedFiles > 0) logger.LogInformation("[import] pruned {Pruned} orphaned upload file(s)", prunedFiles);

        logger.LogInformation("[import] done in {Ms} ms: +{Todos} todos (~{Updated} updated), +{Comments} comments, +{Sessions} sessions, +{Overlaps} overlaps, +{Logs} logs (mode={Mode})",
            sw.ElapsedMilliseconds, addedTodos, updatedTodos, addedComments, addedSessions, addedOverlaps, addedLogs, mode);
        return Ok(new { todos = addedTodos, updatedTodos, comments = addedComments, sessions = addedSessions, overlaps = addedOverlaps, logs = addedLogs, mode });
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

    static void CopyBookmark(FilterBookmark from, FilterBookmark to)
    {
        to.Name = from.Name;
        to.Color = from.Color;
        to.NameFilter = from.NameFilter;
        to.ListFilter = from.ListFilter;
        to.StatusFilter = from.StatusFilter;
        to.PrioritaExcluded = from.PrioritaExcluded;
        to.RelatedFilter = from.RelatedFilter;
        to.DetailRelatedFilter = from.DetailRelatedFilter;
        to.DateFrom = from.DateFrom;
        to.DateTo = from.DateTo;
        to.CollapsedIds = from.CollapsedIds;
    }

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
}
