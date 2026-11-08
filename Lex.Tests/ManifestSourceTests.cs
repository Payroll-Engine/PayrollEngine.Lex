using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ManifestSourceTests
{
    private const string Url = "https://example.org/state-rates.pdf";

    private const string Table =
        "[{\"Over\":0,\"Rate\":0.02},{\"Over\":12500,\"Rate\":0.035},{\"Over\":25000,\"Rate\":0.0475},{\"Over\":60000,\"Rate\":0.0525}]";

    private static Manifest Manifest(Entry entry, params Exclusion[] exclusions)
    {
        var manifest = TestSatellite.Manifest(entry);
        manifest.Files.Add(new EvidenceFile { FileName = "rates.pdf", SourceUrls = [Url] });
        manifest.Exclusions.AddRange(exclusions);
        return manifest;
    }

    private static Entry TableEntry(string contentFile = "rates.json") => new()
    {
        Key = "StateRates.XX",
        ContentFile = contentFile,
        Unit = "USD / rate",
        SourceUrls = [Url],
        DocumentRef = "rates.pdf"
    };

    private static void WriteEvidence(TestSatellite s) =>
        File.WriteAllBytes(Path.Combine(s.LexDirectory, "rates.pdf"), TestDocuments.Pdf(
            [["State rates 2026"], ["Up to 12,500: 2%"], ["12,500 to 25,000: 3.5%"]],
            [["25,000 to 60,000: 4.75%"], ["Over 60,000: 5.25%"]]));

    [Fact]
    public async Task ContentFile_TableOverTwoPages_IsFound()
    {
        using var s = new TestSatellite();
        WriteEvidence(s);
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "rates.json"), Table);

        var result = Assert.Single(await ManifestVerifier.VerifyManifest(Manifest(TableEntry()), s.LexDirectory));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal(7, result.NumbersTotal);
        Assert.False(result.Sampled);
        Assert.Equal("rates.pdf: Sections \"Page 1\", \"Page 2\"", result.Location);
        Assert.Equal("rates.pdf", result.Evidence);
        Assert.True(result.CanWrite);
    }

    [Fact]
    public async Task ContentFile_Missing_IsNoContent()
    {
        using var s = new TestSatellite();
        WriteEvidence(s);

        var result = Assert.Single(await ManifestVerifier.VerifyManifest(Manifest(TableEntry("absent.json")), s.LexDirectory));

        Assert.Equal(SourceMatch.NoContent, result.Match);
        Assert.Contains("contentFile missing", result.Reason);
    }

    [Fact]
    public async Task ContentFile_NotJson_IsNoContent()
    {
        using var s = new TestSatellite();
        WriteEvidence(s);
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "rates.csv"), "Over;Rate\n12500;0.035");

        var result = Assert.Single(await ManifestVerifier.VerifyManifest(Manifest(TableEntry("rates.csv")), s.LexDirectory));

        Assert.Equal(SourceMatch.NoContent, result.Match);
        Assert.Contains("not JSON", result.Reason);
    }

    [Fact]
    public async Task ExcludedEntry_IsVerifiedButNotWritable()
    {
        using var s = new TestSatellite();
        WriteEvidence(s);
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "rates.json"), Table);
        var manifest = Manifest(TableEntry(),
            new Exclusion { Kind = Exclusion.KindAutoVerification, Pattern = "StateRates", Reason = "verified by payroll expert" });

        var result = Assert.Single(await ManifestVerifier.VerifyManifest(manifest, s.LexDirectory));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("verified by payroll expert", result.ExcludedReason);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public async Task EntryWithoutLocalEvidence_IsNoEvidence()
    {
        using var s = new TestSatellite();
        var entry = TestSatellite.Value("Rates.A", "8.55");

        var result = Assert.Single(await ManifestVerifier.VerifyManifest(TestSatellite.Manifest(entry), s.LexDirectory));

        Assert.Equal(SourceMatch.NoEvidence, result.Match);
        Assert.Null(result.Evidence);
    }
}
