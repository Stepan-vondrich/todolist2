using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TodoApi.Services;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace TodoApi.Tests;

public class AttachmentTextExtractorTests
{
    // ── plain text formats ────────────────────────────────────────────────────

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("data.csv")]
    [InlineData("config.json")]
    [InlineData("readme.md")]
    [InlineData("server.log")]
    [InlineData("feed.xml")]
    public void Extract_ReturnsRawText_ForPlainTextFormats(string fileName)
    {
        var content = "hledany termin uvnitr souboru";
        var bytes = Encoding.UTF8.GetBytes(content);
        var text = AttachmentTextExtractor.Extract(bytes, fileName);
        Assert.NotNull(text);
        Assert.Contains("hledany termin", text);
    }

    [Fact]
    public void Extract_HandlesUtf8WithDiacritics()
    {
        var bytes = Encoding.UTF8.GetBytes("příliš žluťoučký kůň");
        var text = AttachmentTextExtractor.Extract(bytes, "cesky.txt");
        Assert.Contains("žluťoučký", text);
    }

    // ── unsupported (binary media) ────────────────────────────────────────────

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.png")]
    [InlineData("clip.mp4")]
    [InlineData("clip.mov")]
    [InlineData("archive.zip")]
    public void Extract_ReturnsNull_ForUnsupportedBinaryFormats(string fileName)
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x01, 0x02 };
        var text = AttachmentTextExtractor.Extract(bytes, fileName);
        Assert.Null(text);
    }

    [Fact]
    public void Extract_ReturnsNull_ForEmptyContent()
    {
        var text = AttachmentTextExtractor.Extract(Array.Empty<byte>(), "empty.txt");
        Assert.Null(text);
    }

    [Fact]
    public void Extract_NeverThrows_OnCorruptOfficeFile()
    {
        // Garbage bytes with a .docx extension must not blow up — just return null.
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var ex = Record.Exception(() => AttachmentTextExtractor.Extract(bytes, "broken.docx"));
        Assert.Null(ex);
    }

    // ── Office Open XML formats ───────────────────────────────────────────────

    [Fact]
    public void Extract_ReadsDocxBodyText()
    {
        var bytes = BuildDocx("Toto je obsah Word dokumentu");
        var text = AttachmentTextExtractor.Extract(bytes, "smlouva.docx");
        Assert.NotNull(text);
        Assert.Contains("obsah Word dokumentu", text);
    }

    [Fact]
    public void Extract_ReadsXlsxCellText()
    {
        var bytes = BuildXlsx("FakturaABC", "polozka123");
        var text = AttachmentTextExtractor.Extract(bytes, "tabulka.xlsx");
        Assert.NotNull(text);
        Assert.Contains("FakturaABC", text);
        Assert.Contains("polozka123", text);
    }

    [Fact]
    public void Extract_ReadsPptxSlideText()
    {
        var bytes = BuildPptx("PrezentaceNadpis");
        var text = AttachmentTextExtractor.Extract(bytes, "slidy.pptx");
        Assert.NotNull(text);
        Assert.Contains("PrezentaceNadpis", text);
    }

    [Fact]
    public void Extract_ReadsPdfText()
    {
        var bytes = BuildPdf("ObsahPdfDokumentu");
        var text = AttachmentTextExtractor.Extract(bytes, "dokument.pdf");
        Assert.NotNull(text);
        Assert.Contains("ObsahPdfDokumentu", text);
    }

    [Fact]
    public void ExtractPdfPages_ReturnsOneEntryPerPage_WithTextOnTheRightPage()
    {
        var bytes = BuildMultiPagePdf("Strana jedna", "Strana dva", "HledanyVyrazNaTreti");
        var pages = AttachmentTextExtractor.ExtractPdfPages(bytes);

        Assert.NotNull(pages);
        Assert.Equal(3, pages!.Count);
        Assert.Contains("HledanyVyrazNaTreti", pages[2]);
        Assert.DoesNotContain("HledanyVyrazNaTreti", pages[0]);
        Assert.DoesNotContain("HledanyVyrazNaTreti", pages[1]);
    }

    [Fact]
    public void ExtractPdfPages_ReturnsNull_ForNonPdf()
    {
        Assert.Null(AttachmentTextExtractor.ExtractPdfPages(System.Text.Encoding.UTF8.GetBytes("hello")));
    }

    [Fact]
    public void Extract_ForPdf_EqualsPagesJoinedBySpace()
    {
        // Upload reads a PDF only once: the flat searchable text is the per-page
        // texts joined with a space. This guards that the merged single-pass
        // extraction stays equivalent to the old flat text.
        var bytes = BuildMultiPagePdf("Strana jedna text", "Strana dva text", "Strana tri text");
        var flat = AttachmentTextExtractor.Extract(bytes, "k.pdf");
        var pages = AttachmentTextExtractor.ExtractPdfPages(bytes);

        Assert.NotNull(flat);
        Assert.NotNull(pages);
        Assert.Equal(string.Join(" ", pages!).Trim(), flat!.Trim());
    }

    // ── builders for valid in-memory Office files ─────────────────────────────

    static byte[] BuildDocx(string paragraph)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text(paragraph)))));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    static byte[] BuildXlsx(params string[] cellValues)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new X.Workbook();
            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = new X.SheetData();
            var row = new X.Row();
            foreach (var v in cellValues)
                row.Append(new X.Cell { DataType = X.CellValues.String, CellValue = new X.CellValue(v) });
            sheetData.Append(row);
            wsPart.Worksheet = new X.Worksheet(sheetData);

            var sheets = wbPart.Workbook.AppendChild(new X.Sheets());
            sheets.Append(new X.Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = "Sheet1" });
            wbPart.Workbook.Save();
        }
        return ms.ToArray();
    }

    static byte[] BuildPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842); // A4
        page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 800), font);
        return builder.Build();
    }

    static byte[] BuildMultiPagePdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var t in pageTexts)
        {
            var page = builder.AddPage(595, 842);
            page.AddText(t, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 800), font);
        }
        return builder.Build();
    }

    static byte[] BuildPptx(string title)
    {
        using var ms = new MemoryStream();
        using (var doc = PresentationDocument.Create(ms, DocumentFormat.OpenXml.PresentationDocumentType.Presentation, true))
        {
            var presPart = doc.AddPresentationPart();
            presPart.Presentation = new P.Presentation();

            var slidePart = presPart.AddNewPart<SlidePart>();
            slidePart.Slide = new P.Slide(
                new P.CommonSlideData(
                    new P.ShapeTree(
                        new P.NonVisualGroupShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                            new P.NonVisualGroupShapeDrawingProperties(),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.GroupShapeProperties(),
                        new P.Shape(
                            new P.NonVisualShapeProperties(
                                new P.NonVisualDrawingProperties { Id = 2, Name = "Title" },
                                new P.NonVisualShapeDrawingProperties(),
                                new P.ApplicationNonVisualDrawingProperties()),
                            new P.ShapeProperties(),
                            new P.TextBody(
                                new A.BodyProperties(),
                                new A.ListStyle(),
                                new A.Paragraph(new A.Run(new A.Text(title)))))))) ;

            var slideIdList = new P.SlideIdList(new P.SlideId { Id = 256U, RelationshipId = presPart.GetIdOfPart(slidePart) });
            presPart.Presentation.Append(slideIdList);
            presPart.Presentation.Save();
        }
        return ms.ToArray();
    }
}
