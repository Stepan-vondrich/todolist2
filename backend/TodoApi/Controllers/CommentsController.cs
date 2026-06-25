using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Models;
using TodoApi.Services;

namespace TodoApi.Controllers;

public record UpdateCommentRequest(string? Text);

[ApiController]
[Route("api/[controller]")]
public class CommentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetByTodo([FromQuery] int todoId) =>
        Ok(await db.Comments
            .Where(c => c.TodoId == todoId)
            .OrderBy(c => c.CreatedAt)
            .Include(c => c.Attachments.OrderBy(a => a.SortOrder))
            .ToListAsync());

    [HttpGet("counts")]
    public async Task<IActionResult> GetCounts() =>
        Ok(await db.Comments
            .GroupBy(c => c.TodoId)
            .Select(g => new { TodoId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TodoId, x => x.Count));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> Create([FromForm] int todoId, [FromForm] string? text)
    {
        var form = Request.Form;

        // Collect all file_N entries in index order
        var fileEntries = form.Files
            .Where(f => f.Name.StartsWith("file_") && f.Length > 0)
            .OrderBy(f => int.TryParse(f.Name["file_".Length..], out var idx) ? idx : 0)
            .ToList();

        if (string.IsNullOrWhiteSpace(text) && fileEntries.Count == 0)
            return BadRequest("Text or file is required.");

        var uploadsDir = TodoApi.DataPaths.Uploads;
        Directory.CreateDirectory(uploadsDir);

        var comment = new Comment
        {
            TodoId = todoId,
            Text = text ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
        };

        db.Comments.Add(comment);
        await db.SaveChangesAsync(); // get the Id before adding attachments

        int sortOrder = 0;
        foreach (var fileEntry in fileEntries)
        {
            var indexStr = fileEntry.Name["file_".Length..];

            var ext = Path.GetExtension(fileEntry.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);
            using (var stream = System.IO.File.Create(filePath))
                await fileEntry.CopyToAsync(stream);

            var isHeic = ext.Equals(".heic", StringComparison.OrdinalIgnoreCase)
                      || ext.Equals(".heif", StringComparison.OrdinalIgnoreCase);
            var type = fileEntry.ContentType.StartsWith("video/") ? "video"
                     : (!isHeic && fileEntry.ContentType.StartsWith("image/")) ? "image"
                     : "file";

            // Index searchable text from document-type attachments (docx/xlsx/pptx/
            // pdf/txt/csv/json/…) so global search can match their contents. Read it
            // back from the file we just wrote; null for media/unsupported formats.
            string? extractedText = null;
            string? pageTextsJson = null;
            if (type == "file")
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // Read the PDF once: per-page texts power the page jump, and the
                    // flat searchable text is just those joined (no second pass).
                    var pages = AttachmentTextExtractor.ExtractPdfPages(bytes);
                    if (pages is not null)
                    {
                        pageTextsJson = System.Text.Json.JsonSerializer.Serialize(pages);
                        extractedText = AttachmentTextExtractor.FlattenPages(pages);
                    }
                }
                else
                {
                    extractedText = AttachmentTextExtractor.Extract(bytes, fileEntry.FileName);
                }
            }

            string? previewPath = null;
            var previewEntry = form.Files[$"preview_{indexStr}"];
            if (previewEntry is not null && previewEntry.Length > 0)
            {
                var pExt = Path.GetExtension(previewEntry.FileName);
                var pName = $"{Guid.NewGuid()}{pExt}";
                var pPath = Path.Combine(uploadsDir, pName);
                using (var pStream = System.IO.File.Create(pPath))
                    await previewEntry.CopyToAsync(pStream);
                previewPath = $"/uploads/{pName}";
            }

            db.CommentAttachments.Add(new CommentAttachment
            {
                CommentId = comment.Id,
                Path = $"/uploads/{fileName}",
                FileName = System.IO.Path.GetFileName(fileEntry.FileName), // original name, sans any path
                Type = type,
                Preview = previewPath,
                SortOrder = sortOrder++,
                ExtractedText = extractedText,
                PageTexts = pageTextsJson,
            });
        }

        await db.SaveChangesAsync();

        var result = await db.Comments
            .Include(c => c.Attachments.OrderBy(a => a.SortOrder))
            .FirstAsync(c => c.Id == comment.Id);

        return CreatedAtAction(nameof(GetByTodo), new { todoId = comment.TodoId }, result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCommentRequest req)
    {
        var comment = await db.Comments.FindAsync(id);
        if (comment is null) return NotFound();
        comment.Text = req.Text ?? string.Empty;
        await db.SaveChangesAsync();
        // return with attachments so frontend type matches
        await db.Entry(comment).Collection(c => c.Attachments).LoadAsync();
        return Ok(comment);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var comment = await db.Comments
            .Include(c => c.Attachments)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (comment is null) return NotFound();

        var uploadsRoot = TodoApi.DataPaths.Uploads;
        foreach (var att in comment.Attachments)
        {
            foreach (var path in new[] { att.Path, att.Preview })
            {
                if (path is null) continue;
                try
                {
                    var filePath = Path.Combine(uploadsRoot, Path.GetFileName(path));
                    if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
                }
                catch { /* ignore */ }
            }
        }

        db.Comments.Remove(comment);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
