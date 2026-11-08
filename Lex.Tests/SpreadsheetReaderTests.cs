using ClosedXML.Excel;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class SpreadsheetReaderTests
{
    private const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Rates.xls is created by Resources/Create-Rates.xls.ps1 (Excel 97-2003 format, saved by Microsoft Excel)</summary>
    private static string XlsPath => Path.Combine(AppContext.BaseDirectory, "Resources", "Rates.xls");

    private static byte[] CreateXlsx()
    {
        using var workbook = new XLWorkbook();
        var rates = workbook.Worksheets.Add("Rates");
        rates.Cell("A1").Value = "Band";
        rates.Cell("B1").Value = "Rate";
        rates.Cell("C1").Value = "Double";
        rates.Cell("A2").Value = "A";
        rates.Cell("B2").Value = 8.5;
        rates.Cell("C2").FormulaA1 = "B2*2";
        rates.Cell("A3").Value = "B";
        rates.Cell("B3").Value = 0.125;
        rates.Cell("A4").Value = "Since";
        rates.Cell("B4").Value = new DateTime(2026, 1, 1);
        rates.Cell("B4").Style.NumberFormat.Format = "yyyy-mm-dd";
        rates.Cell("A5").Value = "Active";
        rates.Cell("B5").Value = true;
        var limits = workbook.Worksheets.Add("Limits");
        limits.Cell("A1").Value = "Ceiling";
        limits.Cell("B1").Value = 66150;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream, new SaveOptions { EvaluateFormulasBeforeSaving = true });
        return stream.ToArray();
    }

    // same content in xls and xlsx: formula as cached value, date as serial number, boolean without text
    private static void AssertRatesSections(List<(string Heading, string Text)> sections)
    {
        Assert.Equal(["Rates", "Limits"], sections.Select(x => x.Heading));
        Assert.Equal("Band Rate Double \nA 8.5 17 \nB 0.125 \nSince 46023 \nActive \n", sections[0].Text);
        Assert.Equal("Ceiling 66150 \n", sections[1].Text);
    }

    [Fact]
    public void Read_Xlsx() =>
        AssertRatesSections(SpreadsheetReader.Read(new MemoryStream(CreateXlsx())));

    [Fact]
    public void Read_Xls()
    {
        using var stream = File.OpenRead(XlsPath);
        AssertRatesSections(SpreadsheetReader.Read(stream));
    }

    [Fact]
    public void Read_RowLimit()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Rows");
        // leading empty rows are not counted
        for (var row = 1; row <= 5005; row++)
        {
            sheet.Cell(row + 3, 1).Value = row;
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var lines = SpreadsheetReader.Read(stream)[0].Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5001, lines.Length);
        Assert.Equal("5001 ", lines[^1]);
    }

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xls")]
    public async Task LoadLocal_Spreadsheet_IsReadable(string extension)
    {
        using var s = new TestSatellite();
        var path = Path.Combine(s.LexDirectory, "rates" + extension);
        if (extension == ".xls")
        {
            File.Copy(XlsPath, path);
        }
        else
        {
            await File.WriteAllBytesAsync(path, CreateXlsx());
        }

        var document = await SourceDocument.LoadLocal(path);
        Assert.True(document.IsReadable);
        var result = SourceVerifier.Verify(new Entry { Key = "Limits.Ceiling", Content = "66150", SourceUrls = ["https://example.org/rates"] }, document);
        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal($"rates{extension}: Section \"Limits\"", result.Location);
    }

    [Theory]
    [InlineData(XlsxMediaType, false)]
    [InlineData("application/vnd.ms-excel", true)]
    public async Task LoadLive_SpreadsheetByMediaType(string mediaType, bool xls)
    {
        var bytes = xls ? await File.ReadAllBytesAsync(XlsPath) : CreateXlsx();
        using var client = new HttpClient(new FileHandler(bytes, mediaType));

        var document = await SourceDocument.LoadLive("https://example.org/download?id=rates", client);

        Assert.True(document.IsReadable);
        Assert.Equal(["Rates", "Limits"], document.Sections.Select(x => x.Heading));
    }

    [Fact]
    public async Task LoadLocal_PassedReaderReplacesBuiltInReader()
    {
        using var s = new TestSatellite();
        var path = Path.Combine(s.LexDirectory, "rates.xlsx");
        await File.WriteAllBytesAsync(path, CreateXlsx());
        var readers = new Dictionary<string, Func<Stream, List<(string Heading, string Text)>>>
        {
            [".xlsx"] = _ => [("Custom", "Value 1")]
        };

        var document = await SourceDocument.LoadLocal(path, readers);

        Assert.Equal([("Custom", "Value 1")], document.Sections);
    }

    [Fact]
    public async Task LoadLocal_InvalidSpreadsheet_IsUnreadable()
    {
        using var s = new TestSatellite();
        var path = Path.Combine(s.LexDirectory, "broken.xlsx");
        await File.WriteAllTextAsync(path, "not a spreadsheet");

        var document = await SourceDocument.LoadLocal(path);

        Assert.False(document.IsReadable);
    }

    [Fact]
    public void TextHash_SameForXlsAndXlsx()
    {
        using var stream = File.OpenRead(XlsPath);
        var xls = new SourceDocument { State = SourceState.Readable, Sections = SpreadsheetReader.Read(stream) };
        var xlsx = new SourceDocument { State = SourceState.Readable, Sections = SpreadsheetReader.Read(new MemoryStream(CreateXlsx())) };
        Assert.Equal(xls.TextHash(), xlsx.TextHash());
    }
}
