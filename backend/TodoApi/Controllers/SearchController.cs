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
                    if (ContainsNormNoSpace(att.ExtractedText, norm))
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
                            // 1-based page where the query first appears (PDFs with a
                            // page index); null lets the viewer scan pages itself.
                            pageNumber = FirstMatchingPage(att.PageTexts, norm),
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

    static string StripSpace(string s) =>
        new(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray());

    // Whitespace-insensitive variant for attachment text: a phrase that spans two
    // lines in a PDF is often extracted with the words fused ("celé\nkuře" →
    // "celékuře") or with extra spaces, so a spaced query would miss it.
    static bool ContainsNormNoSpace(string? field, string normQuery)
    {
        if (string.IsNullOrEmpty(field)) return false;
        var q = StripSpace(normQuery);
        if (q.Length == 0) return false;
        return StripSpace(Normalize(field)).Contains(q, StringComparison.Ordinal);
    }

    // The 1-based page number where the query first appears, from a JSON string[]
    // of per-page text. Null when there's no page index (non-PDF or legacy file).
    static int? FirstMatchingPage(string? pageTextsJson, string normQuery)
    {
        if (string.IsNullOrEmpty(pageTextsJson)) return null;
        try
        {
            var pages = System.Text.Json.JsonSerializer.Deserialize<List<string>>(pageTextsJson);
            if (pages is null) return null;
            // First a page that contains the whole query on its own.
            for (int i = 0; i < pages.Count; i++)
                if (ContainsNormNoSpace(pages[i], normQuery)) return i + 1;
            // Otherwise a phrase may straddle a page break (end of page i, start of
            // i+1). Check each adjacent pair and return the first of the two.
            for (int i = 0; i + 1 < pages.Count; i++)
                if (ContainsNormNoSpace(pages[i] + " " + pages[i + 1], normQuery)) return i + 1;
        }
        catch { /* malformed JSON → no page hint */ }
        return null;
    }

    // A short context window around the first match, so the UI shows where the
    // term appears inside a large file rather than dumping the whole document.
    static string Snippet(string text, string normQuery, int radius = 80)
    {
        var normText = Normalize(text);
        var idx = normText.IndexOf(normQuery, StringComparison.Ordinal);
        // Fall back to a whitespace-insensitive search so the snippet still lands
        // near the match when the words are fused/spaced differently in the source.
        if (idx < 0)
        {
            var q = StripSpace(normQuery);
            if (q.Length > 0)
            {
                // Build the spaceless text plus a map back to positions in normText.
                var noSpace = new StringBuilder(normText.Length);
                var origPos = new List<int>(normText.Length);
                for (int i = 0; i < normText.Length; i++)
                    if (!char.IsWhiteSpace(normText[i])) { noSpace.Append(normText[i]); origPos.Add(i); }
                var hit = noSpace.ToString().IndexOf(q, StringComparison.Ordinal);
                if (hit >= 0) idx = origPos[hit];
            }
        }
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
