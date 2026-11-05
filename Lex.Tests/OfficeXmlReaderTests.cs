using System.IO.Compression;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class OfficeXmlReaderTests
{
    private const string OdfNamespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\"";

    private const string WordNamespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"";

    private const string OdsMimeType = "application/vnd.oasis.opendocument.spreadsheet";
    private const string OdtMimeType = "application/vnd.oasis.opendocument.text";

    private static byte[] Ods(string body) => TestDocuments.Zip(
        ("mimetype", OdsMimeType),
        ("content.xml", $"<office:document-content {OdfNamespaces}><office:body><office:spreadsheet>{body}</office:spreadsheet></office:body></office:document-content>"));

    private static byte[] Odt(string body) => TestDocuments.Zip(
        ("mimetype", OdtMimeType),
        ("content.xml", $"<office:document-content {OdfNamespaces}><office:body><office:text>{body}</office:text></office:body></office:document-content>"));

    private static byte[] Docx(string body, string styles = null)
    {
        var entries = new List<(string, string)>
        {
            ("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>"),
            ("word/document.xml", $"<w:document {WordNamespaces}><w:body>{body}</w:body></w:document>")
        };
        if (styles != null)
        {
            entries.Add(("word/styles.xml", $"<w:styles {WordNamespaces}>{styles}</w:styles>"));
        }
        return TestDocuments.Zip([.. entries]);
    }

    private static string Cell(string value, string attributes = "") =>
        $"<table:table-cell {attributes}><text:p>{value}</text:p></table:table-cell>";

    private static readonly byte[] RatesOds = Ods(
        "<table:table table:name=\"Rates\">" +
        $"<table:table-row>{Cell("Band", "office:value-type=\"string\"")}{Cell("Rate", "office:value-type=\"string\"")}</table:table-row>" +
        $"<table:table-row>{Cell("A", "office:value-type=\"string\"")}{Cell("8,50 %", "office:value-type=\"percentage\" office:value=\"0.085\"")}</table:table-row>" +
        "<table:table-row table:number-rows-repeated=\"1048000\"><table:table-cell table:number-columns-repeated=\"1024\"/></table:table-row>" +
        "<table:table-row>" +
        Cell("01.01.2026", "office:value-type=\"date\" office:date-value=\"2026-01-01\"") +
        Cell("TRUE", "office:value-type=\"boolean\" office:boolean-value=\"true\"") +
        "<table:table-cell office:value-type=\"float\" office:value=\"66150\"><text:p>66.150</text:p>" +
        "<office:annotation><text:p>note 999</text:p></office:annotation></table:table-cell>" +
        "</table:table-row></table:table>" +
        "<table:table table:name=\"Limits\"><table:table-row>" +
        Cell("12.570,50", "office:value-type=\"float\" office:value=\"12570.5\" table:number-columns-repeated=\"2\"") +
        "</table:table-row></table:table>");

    [Fact]
    public void ReadOds_SheetsRowsAndTypedValues()
    {
        var sections = OfficeXmlReader.ReadOds(new MemoryStream(RatesOds));

        Assert.Equal(["Rates", "Limits"], sections.Select(x => x.Heading));
        // percentage as value, date as serial number, boolean without text, repeated empty rows skipped
        Assert.Equal("Band Rate \nA 0.085 \n46023 66150 \n", sections[0].Text);
        Assert.Equal("12570.5 12570.5 \n", sections[1].Text);
    }

    [Fact]
    public void ReadOds_RepeatedRowWithValue_IsLimited()
    {
        var ods = Ods("<table:table table:name=\"Rows\">" +
                      $"<table:table-row table:number-rows-repeated=\"100000\">{Cell("7", "office:value-type=\"float\" office:value=\"7\"")}</table:table-row>" +
                      "</table:table>");

        var lines = OfficeXmlReader.ReadOds(new MemoryStream(ods))[0].Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(100, lines.Length);
    }

    private static readonly byte[] RatesOdt = Odt(
        "<text:p>Intro 2026</text:p>" +
        "<text:h text:outline-level=\"1\">Rates</text:h>" +
        "<text:p>The standard rate is <text:span>8.5</text:span><text:s/>%.</text:p>" +
        "<table:table><table:table-row>" +
        "<table:table-cell><text:p>Band A</text:p></table:table-cell><table:table-cell><text:p>20 %</text:p></table:table-cell>" +
        "</table:table-row><table:table-row>" +
        "<table:table-cell><text:p>Band B</text:p></table:table-cell><table:table-cell><text:p>40</text:p><text:p>%</text:p></table:table-cell>" +
        "</table:table-row></table:table>" +
        "<text:h text:outline-level=\"5\">Minor heading 7</text:h>" +
        "<text:tracked-changes><text:changed-region><text:deletion><text:p>Old rate 9.9</text:p></text:deletion></text:changed-region></text:tracked-changes>" +
        "<text:h text:outline-level=\"2\">Limits</text:h>" +
        "<text:p>Ceiling 66,150<office:annotation><text:p>comment 123</text:p></office:annotation><text:line-break/>Floor 1,000</text:p>");

    [Fact]
    public void ReadOdt_SectionsByHeading_TableRowsOnOneLine()
    {
        var sections = OfficeXmlReader.ReadOdt(new MemoryStream(RatesOdt));

        Assert.Equal(
        [
            (OfficeXmlReader.StartHeading, "Intro 2026"),
            ("Rates", "The standard rate is 8.5 %.\nBand A\t20 %\nBand B\t40 %\nMinor heading 7"),
            ("Limits", "Ceiling 66,150\nFloor 1,000")
        ], sections);
    }

    private const string WordStyles =
        "<w:docDefaults><w:pPrDefault><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        // localized style id of the built-in heading style
        "<w:style w:type=\"paragraph\" w:styleId=\"berschrift1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Custom\"><w:name w:val=\"Custom heading\"/><w:basedOn w:val=\"berschrift1\"/></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"Strong\"><w:name w:val=\"heading 2\"/></w:style>";

    private static string Paragraph(string text, string properties = "") =>
        $"<w:p>{(properties.Length > 0 ? $"<w:pPr>{properties}</w:pPr>" : string.Empty)}<w:r><w:t>{text}</w:t></w:r></w:p>";

    private static readonly byte[] RatesDocx = Docx(
        Paragraph("Intro 2026") +
        // ReSharper disable StringLiteralTypo
        Paragraph("Rates", "<w:pStyle w:val=\"berschrift1\"/>") +
        "<w:p><w:r><w:t xml:space=\"preserve\">The standard rate is </w:t></w:r><w:r><w:t>8.5</w:t></w:r><w:r><w:tab/><w:t>%</w:t></w:r>" +
        "<w:del><w:r><w:delText>9.9</w:delText></w:r></w:del><w:r><w:instrText> PAGE 77 </w:instrText></w:r></w:p>" +
        "<w:tbl><w:tr><w:tc>" + Paragraph("Band A") + "</w:tc><w:tc>" + Paragraph("20 %") + "</w:tc></w:tr></w:tbl>" +
        "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:t>Box 1</w:t></mc:Choice>" +
        "<mc:Fallback><w:t>Box 1</w:t></mc:Fallback></mc:AlternateContent></w:r></w:p>" +
        Paragraph("Limits", "<w:pStyle w:val=\"Custom\"/>") +
        "<w:p><w:r><w:t>Ceiling 66,150</w:t><w:br/><w:t>Line two</w:t></w:r></w:p>" +
        Paragraph("Not a heading 5", "<w:pStyle w:val=\"berschrift1\"/><w:outlineLvl w:val=\"9\"/>") +
        // ReSharper restore StringLiteralTypo
        Paragraph("Notes", "<w:outlineLvl w:val=\"2\"/>") +
        Paragraph("Note 3", "<w:pStyle w:val=\"Strong\"/>"),
        WordStyles);

    [Fact]
    public void ReadDocx_SectionsByOutlineLevel_TableRowsOnOneLine()
    {
        var sections = OfficeXmlReader.ReadDocx(new MemoryStream(RatesDocx));

        Assert.Equal(
        [
            (OfficeXmlReader.StartHeading, "Intro 2026"),
            ("Rates", "The standard rate is 8.5 %\nBand A\t20 %\nBox 1"),
            ("Limits", "Ceiling 66,150\nLine two\nNot a heading 5"),
            ("Notes", "Note 3")
        ], sections);
    }

    [Fact]
    public void ReadDocx_WithoutStyles_DirectOutlineLevelOnly()
    {
        var sections = OfficeXmlReader.ReadDocx(new MemoryStream(Docx(
            Paragraph("Rates", "<w:pStyle w:val=\"Heading1\"/>") + Paragraph("Limits", "<w:outlineLvl w:val=\"0\"/>") + Paragraph("Ceiling 66,150"))));

        Assert.Equal([(OfficeXmlReader.StartHeading, "Rates"), ("Limits", "Ceiling 66,150")], sections);
    }

    [Theory]
    [InlineData("rates.ods", "Limits", "12570.5")]
    [InlineData("rates.odt", "Limits", "66150")]
    [InlineData("rates.docx", "Limits", "66150")]
    [InlineData("rates", "Rates", "0.085")]
    public async Task LoadLocal_OfficeDocument_IsFound(string fileName, string section, string content)
    {
        var bytes = Path.GetExtension(fileName) switch
        {
            ".odt" => RatesOdt,
            ".docx" => RatesDocx,
            _ => RatesOds
        };
        using var s = new TestSatellite();
        var path = Path.Combine(s.LexDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);

        var document = await SourceDocument.LoadLocal(path);

        var result = SourceVerifier.Verify(new Entry { Key = "Rates.A", Content = content, SourceUrls = ["https://example.org/doc"] }, document);
        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal($"Section \"{section}\"", result.Location);
    }

    [Theory]
    [InlineData("deleted 9.9")]
    [InlineData("field code 77")]
    [InlineData("comment 123")]
    [InlineData("note 999")]
    public async Task LoadLocal_SkippedContent_IsNotFound(string text)
    {
        var number = text.Split(' ')[^1];
        foreach (var bytes in new[] { RatesOds, RatesOdt, RatesDocx })
        {
            using var s = new TestSatellite();
            var path = Path.Combine(s.LexDirectory, "document");
            await File.WriteAllBytesAsync(path, bytes);
            var document = await SourceDocument.LoadLocal(path);
            Assert.True(document.IsReadable);
            Assert.Equal(SourceMatch.NotFound,
                SourceVerifier.Verify(new Entry { Key = "Rates.A", Content = number, SourceUrls = ["https://example.org/doc"] }, document).Match);
        }
    }

    [Theory]
    [InlineData(OdsMimeType, "styles.xml", "<x/>", "content.xml not found")]
    [InlineData(OdtMimeType, "content.xml", "<office:document-content", null)]
    [InlineData(OdtMimeType, "content.xml", "<!DOCTYPE x [<!ENTITY e \"8.5\">]><x>&e;</x>", null)]
    public async Task LoadLocal_DamagedDocument_IsUnreadable(string mimeType, string entry, string content, string reason)
    {
        using var s = new TestSatellite();
        var path = Path.Combine(s.LexDirectory, "broken.odt");
        await File.WriteAllBytesAsync(path, TestDocuments.Zip(("mimetype", mimeType), (entry, content)));

        var document = await SourceDocument.LoadLocal(path);

        Assert.Equal(SourceState.Unreadable, document.State);
        if (reason != null)
        {
            Assert.Equal(reason, document.Reason);
        }
    }

    [Fact]
    public void ReadDocx_PartLargerThanLimit_Throws()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("word/document.xml", CompressionLevel.Fastest).Open());
            var chunk = new string(' ', 1024 * 1024);
            for (var i = 0; i <= OfficeXmlReader.MaxPartMegabytes; i++)
            {
                writer.Write(chunk);
            }
        }
        stream.Position = 0;

        var exception = Assert.Throws<InvalidDataException>(() => OfficeXmlReader.ReadDocx(stream));
        Assert.Equal($"word/document.xml exceeds {OfficeXmlReader.MaxPartMegabytes} MB", exception.Message);
    }
}
