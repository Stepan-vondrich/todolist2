using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace TodoApi.Services;

/// <summary>
/// Extracts searchable plain text from an uploaded attachment's bytes, based on
/// its file extension. Returns null when the format carries no text (images,
/// video, archives) or when extraction fails — callers treat null as "nothing
/// to index" and never surface an error to the user.
/// </summary>
public static class AttachmentTextExtractor
{
    // Extensions we read verbatim as UTF-8 text.
    static readonly HashSet<string> PlainText = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".tsv", ".json", ".md", ".markdown", ".log",
        ".xml", ".yaml", ".yml", ".html", ".htm", ".rtf", ".ini",
    };

    // Cap stored text so a giant file can't bloat the DB or a search scan.
    const int MaxChars = 1_000_000;

    public static string? Extract(byte[] bytes, string fileName)
    {
        if (bytes is null || bytes.Length == 0) return null;
        var ext = Path.GetExtension(fileName);

        try
        {
            string? text = ext.ToLowerInvariant() switch
            {
                ".docx" => ExtractDocx(bytes),
                ".xlsx" => ExtractXlsx(bytes),
                ".pptx" => ExtractPptx(bytes),
                ".pdf"  => ExtractPdf(bytes),
                _ when PlainText.Contains(ext) => DecodeText(bytes),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(text)) return null;
            return text.Length > MaxChars ? text[..MaxChars] : text;
        }
        catch
        {
            // Corrupt or unexpected file shape — nothing searchable, but never throw.
            return null;
        }
    }

    static string DecodeText(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    static string ExtractDocx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";
        // InnerText concatenates every run; join paragraphs with spaces so words
        // at element boundaries don't fuse together.
        var sb = new StringBuilder();
        foreach (var para in body.Descendants<W.Paragraph>())
            sb.Append(para.InnerText).Append(' ');
        return sb.ToString();
    }

    static string ExtractXlsx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);
        var wbPart = doc.WorkbookPart;
        if (wbPart is null) return "";

        // Shared strings table holds most cell text in xlsx.
        var shared = wbPart.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();

        foreach (var wsPart in wbPart.WorksheetParts)
        {
            foreach (var cell in wsPart.Worksheet.Descendants<X.Cell>())
            {
                var raw = cell.CellValue?.InnerText;
                if (string.IsNullOrEmpty(raw)) { sb.Append(cell.InnerText).Append(' '); continue; }

                if (cell.DataType?.Value == X.CellValues.SharedString
                    && shared is not null
                    && int.TryParse(raw, out var idx)
                    && idx >= 0 && idx < shared.ChildElements.Count)
                {
                    sb.Append(shared.ChildElements[idx].InnerText).Append(' ');
                }
                else
                {
                    sb.Append(raw).Append(' ');
                }
            }
        }
        return sb.ToString();
    }

    static string ExtractPptx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = PresentationDocument.Open(ms, false);
        var presPart = doc.PresentationPart;
        if (presPart is null) return "";

        var sb = new StringBuilder();
        foreach (var slidePart in presPart.SlideParts)
            foreach (var t in slidePart.Slide.Descendants<A.Text>())
                sb.Append(t.Text).Append(' ');
        return sb.ToString();
    }

    static string ExtractPdf(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var pdf = PdfDocument.Open(ms);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
            sb.Append(page.Text).Append(' ');
        return sb.ToString();
    }
}
