using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class PdfReaderTests
{
    private const string Hex32 = "<0000000000000000000000000000000000000000000000000000000000000000>";

    private static List<(string Heading, string Text)> Read(byte[] pdf) =>
        PdfReader.Read(new MemoryStream(pdf));

    private static SourceResult Verify(SourceDocument document, string content, string unit = null) =>
        SourceVerifier.Verify(new Entry { Key = "Rates.A", Content = content, Unit = unit, SourceUrls = ["https://example.org/doc"] }, document);

    private static async Task<SourceDocument> LoadLocal(string fileName, byte[] bytes)
    {
        using var s = new TestSatellite();
        var path = Path.Combine(s.LexDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        return await SourceDocument.LoadLocal(path);
    }

    [Fact]
    public void Read_Text_OneSectionPerPage()
    {
        var sections = Read(TestDocuments.TextPdf("Standard rate 8.5 %", "Ceiling 66,150"));

        var (heading, text) = Assert.Single(sections);
        Assert.Equal("Page 1", heading);
        Assert.Equal("Standard rate 8.5 %\nCeiling 66,150", text);
    }

    [Fact]
    public void Read_TableRow_StaysOnOneLine()
    {
        var text = Assert.Single(Read(TestDocuments.Pdf([["Band A", "20 %"], ["Band B", "40 %"]]))).Text;

        var lines = text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("Band A", lines[0]);
        Assert.Contains("20 %", lines[0]);
        Assert.Contains("Band B", lines[1]);
        Assert.Contains("40 %", lines[1]);
    }

    [Fact]
    public void Read_PageWithoutText_IsSkipped()
    {
        var sections = Read(TestDocuments.Pdf([["Rate 8.5"]], [], [["Ceiling 66150"]]));

        Assert.Equal(["Page 1", "Page 3"], sections.Select(x => x.Heading));
    }

    [Fact]
    public void Read_FormField_IsFormFieldSection()
    {
        var sections = Read(TestDocuments.PdfWithTextField("Ceiling", "66150.00"));

        var (heading, text) = Assert.Single(sections);
        Assert.Equal(PdfReader.FormFieldsHeading, heading);
        Assert.Equal("66150.00", text);
    }

    [Fact]
    public void Read_NoTextLayer_Throws()
    {
        var exception = Assert.Throws<InvalidDataException>(() => Read(TestDocuments.Pdf([], [])));
        Assert.Equal("no text layer (scan?)", exception.Message);
    }

    [Fact]
    public void Read_UserPassword_IsEncrypted()
    {
        // the empty password matches neither the user nor the owner password
        var pdf = TestDocuments.PdfWithTextField("Ceiling", "66150.00",
            // ReSharper disable StringLiteralTypo
            "/Encrypt 5 0 R /ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>]",
            // ReSharper restore StringLiteralTypo
            $"<< /Filter /Standard /V 1 /R 2 /Length 40 /O {Hex32} /U {Hex32} /P -4 >>");

        var exception = Assert.Throws<InvalidDataException>(() => Read(pdf));
        Assert.Equal("encrypted", exception.Message);
    }

    [Fact]
    public void Read_TooManyPages_Throws()
    {
        var pages = Enumerable.Range(0, PdfReader.MaxPages + 1).Select(_ => Array.Empty<string[]>()).ToArray();

        var exception = Assert.Throws<InvalidDataException>(() => Read(TestDocuments.Pdf(pages)));
        Assert.StartsWith($"PDF exceeds {PdfReader.MaxPages} pages", exception.Message);
    }

    [Fact]
    public void Read_TooLarge_Throws()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            PdfReader.Read(new LengthStream(PdfReader.MaxMegabytes * 1024L * 1024L + 1)));
        Assert.Equal($"PDF exceeds {PdfReader.MaxMegabytes} MB", exception.Message);
    }

    [Theory]
    [InlineData("%PDF-1.4\nthis is not a PDF body")]
    // ReSharper disable once StringLiteralTypo
    [InlineData("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF")]
    public async Task Load_DamagedPdf_IsUnreadable(string content)
    {
        var document = await LoadLocal("broken.pdf", Encoding.ASCII.GetBytes(content));

        Assert.Equal(SourceState.Unreadable, document.State);
        Assert.False(string.IsNullOrWhiteSpace(document.Reason));
        Assert.Equal(SourceMatch.Unreadable, Verify(document, "8.5").Match);
    }

    [Fact]
    public async Task Load_PdfWithoutExtension_IsReadable()
    {
        var document = await LoadLocal("evidence", TestDocuments.Pdf([["Rate 8.5 %"]], [["Ceiling 66,150"]]));

        Assert.True(document.IsReadable);
        var result = Verify(document, "66150");
        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("evidence: Section \"Page 2\"", result.Location);
    }

    [Fact]
    public async Task Load_PdfOnSeveralPages_LocationNamesPages()
    {
        var document = await LoadLocal("rates.pdf", TestDocuments.Pdf([["Rate 8.5 %"]], [["Ceiling 66,150"]]));

        var result = Verify(document, "{ \"rate\": 0.085, \"ceiling\": 66150 }", "mixed / rate");

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("rates.pdf: Sections \"Page 1\", \"Page 2\"", result.Location);
    }

    [Fact]
    public async Task Load_FormFieldValue_IsFound()
    {
        var document = await LoadLocal("form.pdf", TestDocuments.PdfWithTextField("Ceiling", "66150.00"));

        var result = Verify(document, "66150");

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("form.pdf: Section \"Form fields\"", result.Location);
    }

    [Fact]
    public async Task Load_NoTextLayer_IsUnreadable()
    {
        var document = await LoadLocal("scan.pdf", TestDocuments.Pdf([[]]));

        Assert.Equal(SourceState.Unreadable, document.State);
        Assert.Equal("no text layer (scan?)", document.Reason);
    }

    [Theory]
    [InlineData("https://example.org/rates.pdf", "application/pdf")]
    [InlineData("https://example.org/download?id=rates", "application/octet-stream")]
    public async Task LoadLive_Pdf_IsReadable(string url, string mediaType)
    {
        using var client = new HttpClient(new FileHandler(TestDocuments.TextPdf("Ceiling 66,150"), mediaType));

        var document = await SourceDocument.LoadLive(url, client);

        Assert.True(document.IsReadable);
        Assert.Equal("Page 1", Assert.Single(document.Sections).Heading);
    }

    private sealed class LengthStream(long length) : MemoryStream
    {
        public override long Length => length;
    }

    private sealed class FileHandler(byte[] bytes, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
