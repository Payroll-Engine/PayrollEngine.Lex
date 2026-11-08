using System.Text;
using ClosedXML.Excel;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class FormatDetectorTests
{
    private static readonly string Filler = string.Concat(Enumerable.Repeat("filler text ", 60));

    private static string XlsPath => Path.Combine(AppContext.BaseDirectory, "Resources", "Rates.xls");

    private static byte[] Xlsx()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Limits").Cell("A1").Value = 66150;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Theory]
    [InlineData("%PDF-1.7\n%binary")]
    [InlineData("\r\n\r\n%PDF-1.4\n")]
    public void Detect_Pdf(string content) =>
        Assert.Equal(".pdf", FormatDetector.Detect(Encoding.ASCII.GetBytes(content)));

    [Fact]
    public void Detect_Pdf_HeaderAfterFirstKilobyte_IsUnknown() =>
        Assert.Null(FormatDetector.Detect(Encoding.ASCII.GetBytes(new string(' ', 1024) + "%PDF-1.7")));

    [Fact]
    public void Detect_Xls() =>
        Assert.Equal(".xls", FormatDetector.Detect(File.ReadAllBytes(XlsPath)));

    [Fact]
    public void Detect_Xlsx() =>
        Assert.Equal(".xlsx", FormatDetector.Detect(Xlsx()));

    [Theory]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet", "content.xml", ".ods")]
    [InlineData("application/vnd.oasis.opendocument.text", "content.xml", ".odt")]
    [InlineData("application/vnd.oasis.opendocument.presentation", "content.xml", ".zip")]
    [InlineData(null, "word/document.xml", ".docx")]
    [InlineData(null, "xl/workbook.bin", ".xlsb")]
    [InlineData(null, "readme.txt", ".zip")]
    public void Detect_ZipContainer(string mimeType, string entry, string expected)
    {
        var entries = new List<(string, string)>();
        if (mimeType != null)
        {
            entries.Add(("mimetype", mimeType));
        }
        entries.Add((entry, "<x/>"));
        Assert.Equal(expected, FormatDetector.Detect(TestDocuments.Zip([.. entries])));
    }

    [Fact]
    public void Detect_DamagedZip_IsZip() =>
        Assert.Equal(".zip", FormatDetector.Detect([0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02]));

    [Theory]
    [InlineData("PK rates 2026")]
    [InlineData("<html><body>8.5%</body></html>")]
    [InlineData("")]
    public void Detect_Text_IsUnknown(string content) =>
        Assert.Null(FormatDetector.Detect(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public void IsBinary_Text_IsFalse()
    {
        Assert.False(FormatDetector.IsBinary("Rate\t8.5 %\r\nLimit 12’570\f"u8.ToArray()));
        // ReSharper disable once StringLiteralTypo
        Assert.False(FormatDetector.IsBinary(Encoding.Latin1.GetBytes("Höchstbetrag 12.570 €")));
        Assert.False(FormatDetector.IsBinary(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Rate 8.5")).ToArray()));
    }

    [Fact]
    public void IsBinary_ZeroByteOrControlCharacters_IsTrue()
    {
        Assert.True(FormatDetector.IsBinary([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]));
        Assert.True(FormatDetector.IsBinary("abc\u0001\u0002def"u8.ToArray()));
    }

    [Fact]
    public void DecodeText_Utf16WithByteOrderMark() =>
        Assert.Equal("Rate 8.5", FormatDetector.DecodeText(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Rate 8.5")).ToArray()));

    private static async Task<SourceDocument> LoadLocal(string fileName, byte[] bytes,
        IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers = null)
    {
        using var s = new TestSatellite();
        var path = Path.Combine(s.LexDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        return await SourceDocument.LoadLocal(path, readers);
    }

    [Fact]
    public async Task Load_SpreadsheetWithOtherExtension_IsReadBySpreadsheetReader()
    {
        var document = await LoadLocal("rates.dat", Xlsx());
        Assert.True(document.IsReadable);
        Assert.Equal("Limits", Assert.Single(document.Sections).Heading);
    }

    [Fact]
    public async Task Load_HtmlWithSpreadsheetExtension_IsReadAsHtml()
    {
        var document = await LoadLocal("rates.xls", Encoding.UTF8.GetBytes(
            $"<html><body><h2>Rates</h2><table><tr><td>A</td><td>8.5</td></tr></table><p>{Filler}</p></body></html>"));
        Assert.True(document.IsReadable);
        Assert.Equal("Rates", document.Sections[0].Heading);
    }

    [Theory]
    [InlineData("archive.zip", ".zip", "ZIP archive")]
    [InlineData("image.png", null, "unknown binary format")]
    [InlineData("image", null, "unknown binary format")]
    public async Task Load_UnknownBinary_IsUnsupported(string fileName, string zipEntry, string reason)
    {
        var bytes = zipEntry != null
            ? TestDocuments.Zip(("readme.txt", Filler))
            : [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, .. Encoding.ASCII.GetBytes(Filler)];

        var document = await LoadLocal(fileName, bytes);

        Assert.Equal(SourceState.Unsupported, document.State);
        Assert.Equal(reason, document.Reason);
        Assert.Equal(SourceMatch.Unreadable,
            SourceVerifier.Verify(new Entry { Key = "Rates.A", Content = "60", SourceUrls = ["https://example.org/doc"] }, document).Match);
    }

    [Fact]
    public async Task Load_PassedReaderForExtension_ReadsUnknownBinary()
    {
        var readers = new Dictionary<string, Func<Stream, List<(string Heading, string Text)>>>
        {
            [".dat"] = _ => [("Custom", "Value 8.5")]
        };

        var document = await LoadLocal("rates.dat", [0x00, 0x01, 0x02], readers);

        Assert.Equal([("Custom", "Value 8.5")], document.Sections);
    }

    [Fact]
    public async Task Load_PassedReaderForDetectedFormat_ReplacesBuiltInReader()
    {
        var readers = new Dictionary<string, Func<Stream, List<(string Heading, string Text)>>>
        {
            [".xlsx"] = _ => [("Custom", "Value 8.5")]
        };

        var document = await LoadLocal("download", Xlsx(), readers);

        Assert.Equal([("Custom", "Value 8.5")], document.Sections);
    }

    [Fact]
    public async Task LoadLive_BinaryWithHtmlMediaType_IsUnsupported()
    {
        using var client = new HttpClient(new FileHandler(TestDocuments.Zip(("readme.txt", Filler)), "text/html"));

        var document = await SourceDocument.LoadLive("https://example.org/page", client);

        Assert.Equal(SourceState.Unsupported, document.State);
    }
}
