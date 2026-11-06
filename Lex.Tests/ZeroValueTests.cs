using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ZeroValueTests
{
    private const string Url = "https://example.org/doc";
    private const string Key = "StateRates.TX";
    private const string Pattern = "StateRates.TX.SDIRate";
    private const string Reason = "TX: no state disability insurance program";

    private const string Bundle = "{\"UIRate\":0.027,\"UIWageBase\":9000,\"SDIRate\":0}";

    private const string SourceText = "The new employer rate is 2.7% of the first $9,000 of wages.";

    private static Entry Entry(string content = Bundle, Verification verification = null) => new()
    {
        Key = Key,
        Content = content,
        Unit = "mixed",
        SourceUrls = [Url],
        SourceLocation = "p. 1",
        LegalReference = "Texas Labor Code § 204.006",
        DocumentRef = "rates.html",
        Verification = verification
    };

    private static Exclusion NotApplicable(string pattern = Pattern, string reason = Reason) =>
        new() { Kind = Exclusion.KindNotApplicable, Pattern = pattern, Reason = reason };

    private static SourceResult Verify(Entry entry, params Exclusion[] exclusions) =>
        SourceVerifier.Verify(entry,
            [(Url, new SourceDocument { State = SourceState.Readable, Sections = [("Page 1", SourceText)] })],
            null, exclusions);

    // source verification -----------------------------------------------------

    [Fact]
    public void ZeroWithoutExclusion_IsHint()
    {
        var result = Verify(Entry());

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal(2, result.ContentNumbers);
        Assert.Equal(["SDIRate"], result.Zero);
        Assert.Equal(["SDIRate"], result.UnjustifiedZero);
        Assert.Empty(result.NotApplicable);
    }

    [Fact]
    public void ZeroWithNotApplicable_IsNoHint()
    {
        var result = Verify(Entry(), NotApplicable());

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal(["SDIRate"], result.Zero);
        Assert.Empty(result.UnjustifiedZero);
        var notApplicable = Assert.Single(result.NotApplicable);
        Assert.Equal("SDIRate", notApplicable.Property);
        Assert.Equal("0", notApplicable.Value);
        Assert.Equal(Reason, notApplicable.Reason);
        Assert.Empty(result.Derived);
    }

    [Fact]
    public void ZeroWithDerived_IsNoHint()
    {
        var derived = new Exclusion { Kind = Exclusion.KindDerived, Pattern = Pattern, Reason = "UIRate - 0.027" };

        var result = Verify(Entry(), derived);

        Assert.Empty(result.UnjustifiedZero);
        Assert.Empty(result.NotApplicable);
    }

    [Fact]
    public void ZeroOfUnreadableSource_IsHint()
    {
        var result = SourceVerifier.Verify(Entry(),
            [(Url, new SourceDocument { State = SourceState.Unreadable, Reason = "scanned PDF" })]);

        Assert.Equal(SourceMatch.Unreadable, result.Match);
        Assert.Equal(["SDIRate"], result.UnjustifiedZero);
    }

    [Theory]
    [InlineData("{\"Rate\":0.027,\"Disability\":{\"Rate\":0.0}}", "Disability.Rate")]
    [InlineData("{\"Rate\":0.027,\"SDIRate\":\"0\"}", "SDIRate")]
    public void ZeroProperty_IsListed(string content, string path) =>
        Assert.Equal([path], Verify(Entry(content)).Zero);

    [Theory]
    [InlineData("0")]
    [InlineData("{\"Rate\":0.027,\"Tiers\":[{\"From\":0,\"Rate\":0}]}")]
    [InlineData("{\"Rate\":0.027,\"Code\":\"TX\"}")]
    public void NoBundleZero_IsNotListed(string content) =>
        Assert.Empty(Verify(Entry(content)).Zero);

    [Fact]
    public async Task VerifyManifest_NotApplicable_IsResolved()
    {
        using var s = new TestSatellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "rates.html"),
            "<html><body><h2>Rates</h2><p>" + SourceText + "</p></body></html>");
        var manifest = TestSatellite.Manifest(Entry());
        manifest.Files.Add(new EvidenceFile { FileName = "rates.html", SourceUrls = [Url] });
        manifest.Exclusions.Add(NotApplicable());

        var result = Assert.Single(await SourceVerifier.VerifyManifest(manifest, s.LexDirectory));

        Assert.Equal("SDIRate", Assert.Single(result.NotApplicable).Property);
        Assert.Empty(result.UnjustifiedZero);
    }

    // validation ---------------------------------------------------------------

    private static List<string> Validate(params Exclusion[] exclusions)
    {
        using var s = new TestSatellite();
        var manifest = TestSatellite.Manifest(new Entry { Key = Key, Content = Bundle, SourceUrls = [Url] });
        manifest.Exclusions.AddRange(exclusions);
        return Validator.Validate(manifest, s.LexDirectory);
    }

    [Fact]
    public void NotApplicableExclusion_IsValid() =>
        Assert.Empty(Validate(NotApplicable()));

    [Fact]
    public void NotApplicableWithoutProperty_IsReported() =>
        Assert.Contains(Validate(NotApplicable(Key)), e => e.Contains("(notApplicable)") && e.Contains("addresses the whole entry"));

    [Fact]
    public void NotApplicableWithoutReason_IsReported() =>
        Assert.Contains(Validate(NotApplicable(reason: " ")), e => e.Contains("the 'reason' is mandatory (why the rule does not apply)"));

    // check --------------------------------------------------------------------

    private static TestSatellite Satellite()
    {
        var s = new TestSatellite();
        s.WriteRegulation("""
            [ { "name": "StateRates", "values": [ { "key": "TX",
              "value": "{\"UIRate\":0.027,\"UIWageBase\":9000,\"SDIRate\":0}" } ] } ]
            """);
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{ \"files\": [ { \"fileName\": \"rates.html\" } ] }");
        File.WriteAllText(Path.Combine(s.LexDirectory, "rates.html"), "<html><body><p>" + SourceText + "</p></body></html>");
        return s;
    }

    private static Manifest Manifest(params Exclusion[] exclusions)
    {
        var manifest = TestSatellite.Manifest(Entry(verification: new Verification
        {
            Mode = Verification.ModeManual, VerifiedOn = "2026-11-06", VerifiedBy = "Jane Doe", Evidence = "rates.html"
        }));
        manifest.Files.Add(new EvidenceFile { FileName = "rates.html" });
        manifest.Exclusions.AddRange(exclusions);
        return manifest;
    }

    [Fact]
    public void Check_ZeroWithoutExclusion_IsFindingOnlyInRelease()
    {
        using var s = Satellite();
        var manifest = Manifest();

        var plain = Checker.Check(manifest, s.LexPath);
        var release = Checker.Check(manifest, s.LexPath, release: true);

        Assert.Equal(1, plain.UnjustifiedZero);
        Assert.Equal(["StateRates.TX (SDIRate)"], plain.UnjustifiedZeroSamples);
        Assert.False(plain.HasFindings);
        Assert.True(release.HasFindings);
        Assert.Contains("Zero values without justification (1)", CheckReport.BuildMarkdown([release]));
    }

    [Fact]
    public void Check_ZeroWithNotApplicable_IsNoFinding()
    {
        using var s = Satellite();

        var release = Checker.Check(Manifest(NotApplicable()), s.LexPath, release: true);

        Assert.Empty(release.ValidationErrors);
        Assert.Equal(0, release.UnjustifiedZero);
        Assert.Equal(1, release.ExcludedNotApplicable);
        Assert.False(release.HasFindings);
        Assert.Contains($"notApplicable `{Pattern}`: {Reason}", release.ExclusionNotes);
        Assert.Contains("1 properties not applicable", CheckReport.BuildMarkdown([release]));
    }

    [Fact]
    public void Check_NotApplicableOnNonZeroProperty_IsFinding()
    {
        using var s = Satellite();

        var result = Checker.Check(Manifest(NotApplicable("StateRates.TX.UIRate")), s.LexPath);

        Assert.Contains(result.ValidationErrors, e => e.Contains("property 'UIRate' of entry 'StateRates.TX' is 0.027, not 0"));
        Assert.Equal(0, result.ExcludedNotApplicable);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void Check_NotApplicableOnMissingProperty_IsFinding()
    {
        using var s = Satellite();

        var result = Checker.Check(Manifest(NotApplicable("StateRates.TX.PFLRate")), s.LexPath);

        Assert.Contains(result.ValidationErrors, e => e.Contains("(notApplicable) 'StateRates.TX.PFLRate' matches no property 'PFLRate'"));
    }
}
