using Xunit;

namespace PayrollEngine.Lex.Tests;

public class PdfLiveSourceTests
{
    private const string DocUrl = "https://example.org/doc";

    private static byte[] Rates(string rate) =>
        TestDocuments.Pdf([["Standard rate", rate + " %"], ["Updated 3 November 2026"]], [["Ceiling", "66,150"]]);

    private static SourceDocument Read(byte[] pdf) =>
        new() { State = SourceState.Readable, Sections = PdfReader.Read(new MemoryStream(pdf)) };

    [Fact]
    public void TextHash_SameContent_IsEqual() =>
        Assert.Equal(Read(Rates("8.5")).TextHash(), Read(Rates("8.5")).TextHash());

    [Fact]
    public void TextHash_ChangedValue_IsDifferent() =>
        Assert.NotEqual(Read(Rates("8.5")).TextHash(), Read(Rates("9.0")).TextHash());

    [Fact]
    public void TextHash_IgnoresDates() =>
        Assert.Equal(Read(Rates("8.5")).TextHash(),
            Read(TestDocuments.Pdf([["Standard rate", "8.5 %"], ["Updated 4 November 2026"]], [["Ceiling", "66,150"]])).TextHash());

    private static async Task<CheckResult> SnapshotAndCheck(byte[] snapshotPdf, byte[] livePdf)
    {
        using var s = new TestSatellite();
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "8.5"));
        using var snapshotClient = new HttpClient(new FileHandler(snapshotPdf, "application/pdf"));
        var snapshot = await new SnapshotWriter().WriteAsync(manifest, s.LexDirectory, s.LexDirectory, dryRun: false,
            loadLiveSource: url => SourceDocument.LoadLive(url, snapshotClient));
        var live = Assert.Single(snapshot.LiveSources);
        Assert.Equal(DocUrl, live.Url);
        Assert.Equal(Read(snapshotPdf).TextHash(), live.TextHash);
        Assert.Null(live.Reason);

        var result = new CheckResult { LexPath = s.LexPath, HasSnapshot = true };
        using var checker = new UrlChecker(new FileHandler(livePdf, "application/pdf"));
        await checker.Apply(manifest, result);
        return result;
    }

    [Fact]
    public async Task CheckUrls_UnchangedPdf_IsNoFinding()
    {
        var result = await SnapshotAndCheck(Rates("8.5"), Rates("8.5"));

        Assert.Equal(1, result.SourcesCompared);
        Assert.Empty(result.ChangedSources);
    }

    [Fact]
    public async Task CheckUrls_ChangedPdf_IsFinding()
    {
        var result = await SnapshotAndCheck(Rates("8.5"), Rates("9.0"));

        Assert.Equal([DocUrl], result.ChangedSources);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public async Task CheckUrls_PdfWithoutTextLayer_IsNotComparable()
    {
        var result = await SnapshotAndCheck(Rates("8.5"), TestDocuments.Pdf([[]]));

        Assert.Equal(1, result.SourcesNotComparable);
        Assert.Empty(result.ChangedSources);
    }
}
