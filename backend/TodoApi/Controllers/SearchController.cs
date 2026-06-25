using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using System.Text;
using System.Globalization;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<object>());

        var norm = Normalize(q.Trim());

        var todos = await db.Todos.ToListAsync();
        var comments = await db.Comments.ToListAsync();
        var attachments = await db.CommentAttachments
            .Where(a => a.ExtractedText != null)
            .ToListAsync();

        var commentsByTodo = comments
            .GroupBy(c => c.TodoId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Map commentId → todoId so attachment hits can be attributed to a todo.
        var todoIdByComment = comments.ToDictionary(c => c.Id, c => c.TodoId);
        var attachmentsByTodo = attachments
            .Where(a => todoIdByComment.ContainsKey(a.CommentId))
            .GroupBy(a => todoIdByComment[a.CommentId])
            .ToDictionary(g => g.Key, g => g.ToList());

        var todoById = todos.ToDictionary(t => t.Id);
        var results = new List<object>();

        foreach (var todo in todos)
        {
            var matches = new List<object>();

            if (ContainsNorm(todo.Title, norm))
                matches.Add(new { source = "title", text = todo.Title });
            if (ContainsNorm(todo.Related, norm))
                matches.Add(new { source = "related", text = todo.Related });
            if (ContainsNorm(todo.DetailRelated, norm))
                matches.Add(new { source = "detailRelated", text = todo.DetailRelated });
            if (ContainsNorm(todo.Priority, norm))
                matches.Add(new { source = "priority", text = todo.Priority });

            if (commentsByTodo.TryGetValue(todo.Id, out var todoComments))
            {
                foreach (var comment in todoComments)
                {
                    if (!string.IsNullOrWhiteSpace(comment.Text) && ContainsNorm(comment.Text, norm))
                        matches.Add(new { source = "comment", text = comment.Text, commentId = comment.Id });
                }
            }

            // Attachment file contents (docx/xlsx/pptx/pdf/txt/csv/json/…), indexed at upload.
            if (attachmentsByTodo.TryGetValue(todo.Id, out var todoAttachments))
            {
                foreach (var att in todoAttachments)
                {
                    if (ContainsNorm(att.ExtractedText, norm))
                        matches.Add(new
                        {
                            source = "attachment",
                            text = Snippet(att.ExtractedText!, norm),
                            commentId = att.CommentId,
                            fileName = FileNameOf(att.Path),          // extension chip, e.g. "PDF"
                            // The original upload name to show as a heading; falls back to the
                            // on-disk Guid name for attachments uploaded before names were kept.
                            displayName = att.FileName ?? System.IO.Path.GetFileName(att.Path),
                            // Served URL of the actual file, so the UI can open it
                            // directly and jump to the page/place of the match.
                            attachmentPath = att.Path,
                        });
                }
            }

            if (matches.Count > 0)
            {
                string? parentTitle = todo.ParentId.HasValue && todoById.TryGetValue(todo.ParentId.Value, out var parent)
                    ? parent.Title : null;
                results.Add(new { todoId = todo.Id, todoTitle = todo.Title, parentId = todo.ParentId, parentTitle, matches });
            }
        }

        return Ok(results);
    }

    static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    static bool ContainsNorm(string? field, string normQuery) =>
        !string.IsNullOrEmpty(field) && Normalize(field).Contains(normQuery, StringComparison.Ordinal);

    // A short context window around the first match, so the UI shows where the
    // term appears inside a large file rather than dumping the whole document.
    static string Snippet(string text, string normQuery, int radius = 80)
    {
        var idx = Normalize(text).IndexOf(normQuery, StringComparison.Ordinal);
        if (idx < 0) return text.Length <= radius * 2 ? text : text[..(radius * 2)] + "…";

        var start = Math.Max(0, idx - radius);
        var end = Math.Min(text.Length, idx + normQuery.Length + radius);
        var slice = text[start..end].Trim();
        return (start > 0 ? "…" : "") + slice + (end < text.Length ? "…" : "");
    }

    // Attachments are stored on disk as "/uploads/{guid}{ext}"; surface just the
    // extension (e.g. ".docx") as a lightweight type hint for the UI.
    static string FileNameOf(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return string.IsNullOrEmpty(ext) ? "soubor" : ext.TrimStart('.').ToUpperInvariant();
    }
}
