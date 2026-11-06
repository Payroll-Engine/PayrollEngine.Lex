using Xunit;

namespace PayrollEngine.Lex.Tests;

public class DerivedTests
{
    private const string Url = "https://example.org/fees.pdf";
    private const string Key = "Fees.2026";
    private const string Pattern = "Fees.2026.Levy";
    private const string Reason = "5% of BaseRate (0.05), Example Act s. 12(2)";

    private const string Bundle = "{\"WageBase\":8000,\"BaseRate\":0.05,\"StandardRebate\":0.042,\"Levy\":0.0025}";

    private const string SourceText = "The contribution rate is 5.0% of the first $8,000 of wages. The standard rebate is 4.2%.";

    private static Entry Entry(string content = Bundle) => new()
    {
        Key = Key,
        Content = content,
        Unit = "mixed",
        SourceUrls = [Url],
        SourceLocation = "p. 1",
        LegalReference = "Example Act s. 12",
        DocumentRef = "fees.html"
    };

    private static Exclusion Derived(string pattern = Pattern, string reason = Reason) =>
        new() { Kind = Exclusion.KindDerived, Pattern = pattern, Reason = reason };

    private static SourceDocument Sections(params string[] texts) =>
        new() { State = SourceState.Readable, Sections = texts.Select((x, i) => ($"Page {i + 1}", x)).ToList() };

    private static SourceResult Verify(Entry entry, string text, params Exclusion[] exclusions) =>
        SourceVerifier.Verify(entry, [(Url, Sections(text))], null, exclusions);

    // source verification -----------------------------------------------------

    [Fact]
    public void BundleWithDerivedProperty_IsFound()
    {
        var result = Verify(Entry(), SourceText, Derived());

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal(3, result.NumbersTotal);
        Assert.True(result.CanWrite);
        var derived = Assert.Single(result.Derived);
        Assert.Equal("Levy", derived.Property);
        Assert.Equal("0.0025", derived.Value);
        Assert.Equal(Reason, derived.Reason);
    }

    [Fact]
    public void BundleWithoutDerivedExclusion_IsPartial()
    {
        var result = Verify(Entry(), SourceText);

        Assert.Equal(SourceMatch.Partial, result.Match);
        Assert.Contains("0.0025", result.Missing);
    }

    [Fact]
    public void AllNumbersDerived_IsNoNumbersWithNote()
    {
        var result = Verify(Entry("{\"Levy\":0.0025}"), SourceText, Derived());

        Assert.Equal(SourceMatch.NoNumbers, result.Match);
        Assert.Equal("all numbers derived: confirm with LexAttest", result.Note);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public void NumberAlsoInPropertyNotDerived_IsLookedUp()
    {
        var result = Verify(Entry("{\"WageBase\":8000,\"Surcharge\":0.0025,\"Levy\":0.0025}"), SourceText, Derived());

        Assert.Equal(SourceMatch.Partial, result.Match);
        Assert.Contains("0.0025", result.Missing);
        Assert.Single(result.Derived);
    }

    [Fact]
    public void NestedDerivedProperty_IsNotLookedUp()
    {
        var content = "{\"WageBase\":8000,\"Rebate\":{\"Standard\":0.042,\"Levy\":0.0025}}";

        var result = Verify(Entry(content), SourceText, Derived("Fees.2026.Rebate.Levy"));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Rebate.Levy", Assert.Single(result.Derived).Property);
    }

    [Fact]
    public void DerivedExclusionOfOtherEntry_IsIgnored()
    {
        var result = Verify(Entry(), SourceText, Derived("Other.2026.Levy"));

        Assert.Equal(SourceMatch.Partial, result.Match);
        Assert.Empty(result.Derived);
    }

    [Fact]
    public async Task VerifyManifest_ContentFile_IsFoundWithDerived()
    {
        using var s = new TestSatellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "fees.html"),
            "<html><body><h2>Fees</h2><p>" + SourceText + "</p><h2>Notes</h2><p>" +
            string.Concat(Enumerable.Repeat("filler text ", 60)) + "</p></body></html>");
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "fees.json"), Bundle);
        var entry = Entry();
        var manifest = TestSatellite.Manifest(new Entry
        {
            Key = entry.Key, ContentFile = "fees.json", Unit = entry.Unit, SourceUrls = entry.SourceUrls,
            DocumentRef = entry.DocumentRef
        });
        manifest.Files.Add(new EvidenceFile { FileName = "fees.html", SourceUrls = [Url] });
        manifest.Exclusions.Add(Derived());

        var result = Assert.Single(await SourceVerifier.VerifyManifest(manifest, s.LexDirectory));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Levy", Assert.Single(result.Derived).Property);
    }

    // verification record ------------------------------------------------------

    [Fact]
    public void AutoVerification_ListsDerivedProperties()
    {
        var result = Verify(Entry(), SourceText, Derived());

        var verification = SourceVerifier.AutoVerification(new DateOnly(2026, 11, 6), "fees.html",
            result.Derived.Select(x => x.Property));

        Assert.Equal(["Levy"], verification.Derived);
    }

    [Fact]
    public void AutoVerification_WithoutDerived_HasNoDerivedField() =>
        Assert.Null(SourceVerifier.AutoVerification(new DateOnly(2026, 11, 6), "fees.html", []).Derived);

    [Fact]
    public void HashInput_IncludesDerived()
    {
        var plain = SourceVerifier.AutoVerification(new DateOnly(2026, 11, 6), "fees.html");
        var derived = SourceVerifier.AutoVerification(new DateOnly(2026, 11, 6), "fees.html", ["Levy"]);

        Assert.DoesNotContain("derived", plain.HashInput());
        Assert.EndsWith("|derived:Levy", derived.HashInput());
        Assert.False(plain.SameAs(derived));
    }

    [Fact]
    public async Task SnapshotHash_ChangesWithDerived()
    {
        using var s = new TestSatellite();
        var date = new DateOnly(2026, 11, 6);
        Entry Verified(Verification verification) => new()
        {
            Key = Key, Content = Bundle, SourceUrls = [Url], Verification = verification
        };

        var plain = await s.WriteSnapshot(TestSatellite.Manifest(Verified(SourceVerifier.AutoVerification(date, null))));
        var derived = await s.WriteSnapshot(TestSatellite.Manifest(
            Verified(SourceVerifier.AutoVerification(date, null, ["Levy"]))));

        Assert.NotEqual(plain.SourceHash, derived.SourceHash);
    }

    [Fact]
    public void Editor_WritesAndRefreshesDerived()
    {
        var editor = ManifestEditor.Parse("{ \"values\": [ { \"key\": \"Fees.2026\", \"content\": \"1\" } ] }");
        var date = new DateOnly(2026, 11, 6);

        Assert.True(editor.SetAutoVerification(Key, SourceVerifier.AutoVerification(date, "fees.html", ["Levy"])));
        Assert.Contains("\"derived\"", editor.ToJson());
        Assert.False(editor.SetAutoVerification(Key, SourceVerifier.AutoVerification(date, "fees.html", ["Levy"])));
        Assert.True(editor.SetAutoVerification(Key, SourceVerifier.AutoVerification(date, "fees.html")));
        Assert.DoesNotContain("\"derived\"", editor.ToJson());
    }

    // validation ---------------------------------------------------------------

    private static List<string> Validate(params Exclusion[] exclusions)
    {
        using var s = new TestSatellite();
        var manifest = TestSatellite.Manifest(Entry());
        manifest.Values[0] = new Entry { Key = Key, Content = Bundle, SourceUrls = [Url] };
        manifest.Exclusions.AddRange(exclusions);
        return Validator.Validate(manifest, s.LexDirectory);
    }

    [Fact]
    public void DerivedExclusion_IsValid() =>
        Assert.Empty(Validate(Derived()));

    [Fact]
    public void DerivedPatternWithoutProperty_IsReported() =>
        Assert.Contains(Validate(Derived(Key)), e => e.Contains("addresses the whole entry") && e.Contains("'autoVerification'"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void DerivedWithoutReason_IsReported(string reason) =>
        Assert.Contains(Validate(Derived(reason: reason)), e => e.Contains("the 'reason' is mandatory (formula and legal reference)"));

    [Fact]
    public void ManualVerificationWithDerived_IsReported()
    {
        using var s = new TestSatellite();
        File.WriteAllText(Path.Combine(s.LexDirectory, "fees.pdf"), "evidence");
        var manifest = TestSatellite.Manifest(new Entry
        {
            Key = Key, Content = Bundle, SourceUrls = [Url],
            Verification = new Verification
            {
                Mode = Verification.ModeManual, VerifiedOn = "2026-11-06", VerifiedBy = "Jane Doe", Evidence = "fees.pdf",
                Derived = ["Levy"]
            }
        });
        manifest.Files.Add(new EvidenceFile { FileName = "fees.pdf" });

        Assert.Contains(Validator.Validate(manifest, s.LexDirectory), e => e.Contains("derived is only allowed in mode 'auto'"));
    }

    // check --------------------------------------------------------------------

    private static CheckResult Check(params Exclusion[] exclusions)
    {
        using var s = new TestSatellite();
        s.WriteRegulation("""
            [ { "name": "Fees", "values": [ { "key": "2026",
              "value": "{\"WageBase\":8000,\"BaseRate\":0.05,\"StandardRebate\":0.042,\"Levy\":0.0025}" } ] } ]
            """);
        var manifest = TestSatellite.Manifest(new Entry
        {
            Key = Key, Content = Bundle, SourceUrls = ["https://example.org/doc"], SourceLocation = "p. 1",
            LegalReference = "Example Act s. 12"
        });
        manifest.Exclusions.AddRange(exclusions);
        return Checker.Check(manifest, s.LexPath);
    }

    [Fact]
    public void Check_DerivedExclusion_IsListedAndCounted()
    {
        var result = Check(Derived());

        Assert.Empty(result.ValidationErrors);
        Assert.Equal(1, result.ExcludedDerived);
        Assert.Contains($"derived `{Pattern}`: {Reason}", result.ExclusionNotes);
        Assert.Contains("1 derived properties", CheckReport.BuildMarkdown([result]));
        Assert.EndsWith(";1;0;0;0", CheckReport.BuildCsv([result]).Split('\n')[1].TrimEnd('\r'));
    }

    [Fact]
    public void Check_DerivedPatternOnMissingProperty_IsFinding()
    {
        var result = Check(Derived("Fees.2026.LevyPerYear"));

        Assert.Contains(result.ValidationErrors, e => e.Contains("matches no property 'LevyPerYear' of entry 'Fees.2026'"));
        Assert.Equal(0, result.ExcludedDerived);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void Check_DerivedPatternOnMissingEntry_IsFinding() =>
        Assert.Contains(Check(Derived("Fees.2025.Levy")).ValidationErrors,
            e => e.Contains("'Fees.2025.Levy' matches no entry"));
}
