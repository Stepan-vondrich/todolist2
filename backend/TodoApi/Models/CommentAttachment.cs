namespace TodoApi.Models;

public class CommentAttachment
{
    public int Id { get; set; }
    public int CommentId { get; set; }
    public string Path { get; set; } = string.Empty;
    // The original upload filename (e.g. "Jednoduše a dokonale.pdf"), shown in the UI.
    // The on-disk file keeps a Guid name to avoid collisions and unsafe characters;
    // null for attachments uploaded before this was tracked (fall back to the Guid).
    public string? FileName { get; set; }
    public string? Type { get; set; }
    public string? Preview { get; set; }
    public int SortOrder { get; set; }
    // Full text extracted from the file at upload time (docx/xlsx/pptx/pdf/txt/csv/json/…),
    // so global search can match the file's contents. Null = not a text-bearing file
    // (image/video) or extraction failed/not yet indexed.
    public string? ExtractedText { get; set; }
    // For PDFs: JSON string[] of per-page text, so search can record which page a
    // hit is on and the viewer can open there directly. Null for non-PDF or legacy
    // attachments (fall back to scanning pages in the browser).
    public string? PageTexts { get; set; }
}
