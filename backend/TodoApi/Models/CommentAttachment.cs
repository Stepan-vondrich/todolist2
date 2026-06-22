namespace TodoApi.Models;

public class CommentAttachment
{
    public int Id { get; set; }
    public int CommentId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Preview { get; set; }
    public int SortOrder { get; set; }
    // Full text extracted from the file at upload time (docx/xlsx/pptx/pdf/txt/csv/json/…),
    // so global search can match the file's contents. Null = not a text-bearing file
    // (image/video) or extraction failed/not yet indexed.
    public string? ExtractedText { get; set; }
}
