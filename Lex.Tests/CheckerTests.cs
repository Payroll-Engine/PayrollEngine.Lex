using System.Net;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class CheckerTests
{
    private const string TwoValues =
        "[ { \"name\": \"Rates\", \"values\": [ { \"key\": \"A\", \"value\": \"7.5\" }, { \"key\": \"B\", \"value\": \"10\" } ] } ]";

    [Fact]
    public void FullCoverage_HasNoFindings()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        var manifest = TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "7.50"), TestSatellite.Value("Rates.B", "10"));
        File.WriteAllText(s.LexPath, "{}");
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Equal(2, result.RegulationValues);
        Assert.Equal(2, result.CoveredDirect);
        Assert.Equal(0, result.Uncovered);
        Assert.Equal(0, result.Orphans);
        Assert.Equal(0, result.Drift);
        Assert.False(result.HasFindings);
    }

    [Fact]
    public void ValueWithoutEntry_IsUncovered()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5")), s.LexPath);

        Assert.Equal(1, result.Uncovered);
        Assert.Contains("Rates.B", result.UncoveredSamples);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void EntryWithoutValue_IsOrphan()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "7.5"), TestSatellite.Value("Rates.B", "10"),
            TestSatellite.Value("Rates.Z", "1")), s.LexPath);

        Assert.Equal(1, result.Orphans);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void OfficialCalculatorEntry_IsNotOrphan()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "7.5"), TestSatellite.Value("Rates.B", "10"),
            TestSatellite.Value("OfficialCalculator.IncomeTax", "https://example.org/calculator")), s.LexPath);

        Assert.Equal(0, result.Orphans);
    }

    private static Manifest WithExclusions(Manifest manifest, params Exclusion[] exclusions) => new()
    {
        Authority = manifest.Authority,
        Regulation = manifest.Regulation,
        ValidFrom = manifest.ValidFrom,
        Provider = manifest.Provider,
        Sources = manifest.Sources,
        Values = manifest.Values,
        Exclusions = [.. exclusions]
    };

    [Fact]
    public void CoverageExclusion_IsNotUncovered()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(WithExclusions(
            TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5")),
            new Exclusion { Kind = Exclusion.KindCoverage, Pattern = "Rates.B", Reason = "test" }), s.LexPath);

        Assert.Equal(0, result.Uncovered);
        Assert.Equal(1, result.ExcludedCoverage);
        Assert.False(result.HasFindings);
    }

    [Fact]
    public void OrphanExclusion_IsNotOrphan_AndUnusedOneIsFinding()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");
        var manifest = TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "7.5"), TestSatellite.Value("Rates.B", "10"),
            TestSatellite.Value("Evidence.X", "1"));

        var result = Checker.Check(WithExclusions(manifest,
            new Exclusion { Kind = Exclusion.KindOrphan, Pattern = "Evidence.*", Reason = "test" }), s.LexPath);
        Assert.Equal(0, result.Orphans);
        Assert.Equal(1, result.ExcludedOrphans);
        Assert.False(result.HasFindings);

        var unused = Checker.Check(WithExclusions(manifest,
            new Exclusion { Kind = Exclusion.KindOrphan, Pattern = "Other.*", Reason = "test" }), s.LexPath);
        Assert.True(unused.HasFindings);
    }

    [Fact]
    public void UnusedExclusion_IsFinding()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(WithExclusions(
            TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"), TestSatellite.Value("Rates.B", "10")),
            new Exclusion { Kind = Exclusion.KindCoverage, Pattern = "Rates.*", Reason = "test" }), s.LexPath);

        Assert.True(result.HasFindings);
    }

    [Fact]
    public void SourceLocationExclusion_IsNotMissing()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(WithExclusions(
            TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5", ""), TestSatellite.Value("Rates.B", "10")),
            new Exclusion { Kind = Exclusion.KindSourceLocation, Pattern = "Rates.A", Reason = "test" }), s.LexPath);

        Assert.Equal(0, result.MissingSourceLocation);
        Assert.Equal(1, result.ExcludedSourceLocation);
        Assert.False(result.HasFindings);
    }

    [Fact]
    public void ScalarDifference_IsDrift()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "8.0"), TestSatellite.Value("Rates.B", "10")), s.LexPath);

        Assert.Equal(1, result.Drift);
        Assert.Equal(2, result.Compared);
    }

    [Fact]
    public void StructuredDifference_NamesAllDifferences()
    {
        using var regulation = System.Text.Json.JsonDocument.Parse(
            "[{\"a\":1,\"code\":\"131\"},{\"a\":2,\"code\":\"132\"},{\"a\":3},{\"a\":4,\"bloc\":\"81\"},{\"a\":5}]");
        const string lex = "[{\"a\":1},{\"a\":2},{\"a\":3},{\"a\":4,\"bloc\":\"82\"},{\"a\":6}]";

        var difference = Checker.Difference(regulation.RootElement, null, lex);

        Assert.StartsWith("4 differences: [0]/code missing in lex; [1]/code missing in lex; [3]/bloc regulation '81' vs lex '82'", difference);
        Assert.EndsWith("(+1 more)", difference);
    }

    [Fact]
    public void StructuredDifference_SingleDifferenceUnchanged()
    {
        using var regulation = System.Text.Json.JsonDocument.Parse("[{\"a\":1,\"code\":\"131\"}]");

        Assert.Equal("[0]/code missing in lex", Checker.Difference(regulation.RootElement, null, "[{\"a\":1}]"));
        Assert.Null(Checker.Difference(regulation.RootElement, null, "[{\"a\":1,\"code\":\"131\"}]"));
    }

    [Fact]
    public void ObjectProperty_IsResolvedBySplitKey()
    {
        using var s = new TestSatellite();
        s.WriteRegulation("[ { \"name\": \"Limits\", \"values\": [ { \"key\": \"K\", \"valueObject\": { \"Max\": 100, \"Rate\": 0.2 } } ] } ]");
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(TestSatellite.Manifest(
            TestSatellite.Value("Limits.K.Max", "100"), TestSatellite.Value("Limits.K.Rate", "0.2")), s.LexPath);

        Assert.Equal(0, result.Orphans + result.UnresolvedSplit);
        Assert.Equal(0, result.Drift);
        Assert.Equal(0, result.Uncovered);
    }

    [Fact]
    public void MissingSourceLocation_IsFindingOnlyWhenRequired()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");
        var manifest = TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "7.5", location: null), TestSatellite.Value("Rates.B", "10"));

        var optional = Checker.Check(manifest, s.LexPath);
        var required = Checker.Check(manifest, s.LexPath, requireLocation: true);

        Assert.Equal(1, optional.MissingSourceLocation);
        Assert.False(optional.HasFindings);
        Assert.True(required.HasFindings);
    }

    [Fact]
    public async Task DeadUrl_IsFinding_BlockedUrl_IsNote()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");
        var a = TestSatellite.Value("Rates.A", "7.5");
        var b = TestSatellite.Value("Rates.B", "10");
        var manifest = TestSatellite.Manifest(
            new Entry { Key = a.Key, Content = a.Content, SourceUrls = ["https://example.org/gone"], SourceLocation = "p. 1", LegalReference = "x" },
            new Entry { Key = b.Key, Content = b.Content, SourceUrls = ["https://example.org/blocked"], SourceLocation = "p. 1", LegalReference = "x" });
        var result = Checker.Check(manifest, s.LexPath);

        using var checker = new UrlChecker(new StatusHandler(r =>
            r.RequestUri!.AbsolutePath == "/gone" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden));
        await checker.Apply(manifest, result);

        Assert.Single(result.DeadUrls);
        // the manifest source (example.org/doc) and the entry URL (example.org/blocked) answer 403
        Assert.Equal(2, result.BlockedUrls);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public async Task AvailableUrl_IsNoFinding()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"), TestSatellite.Value("Rates.B", "10"));
        var result = Checker.Check(manifest, s.LexPath);

        using var checker = new UrlChecker(new StatusHandler(_ => HttpStatusCode.OK));
        await checker.Apply(manifest, result);

        Assert.Empty(result.DeadUrls);
        Assert.False(result.HasFindings);
    }

    [Fact]
    public void MissingSnapshot_IsFinding()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues);

        var result = Checker.Check(TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "7.5"), TestSatellite.Value("Rates.B", "10")), s.LexPath);

        Assert.False(result.HasSnapshot);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void ValidFromMismatch_IsFinding()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(TwoValues, validFrom: "2025-01-01");
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var result = Checker.Check(TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "7.5"), TestSatellite.Value("Rates.B", "10")), s.LexPath);

        Assert.False(result.ValidFromMatch);
        Assert.True(result.HasFindings);
    }

    [Theory]
    [InlineData("7.5", "7.50", true)]
    [InlineData("7.5", "7.6", false)]
    [InlineData("ABC", "abc", true)]
    public void ScalarEquals_ComparesNumbersAndIgnoresCase(string a, string b, bool expected) =>
        Assert.Equal(expected, Checker.ScalarEquals(a, b));
}
