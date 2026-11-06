using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ReferenceYearTests
{
    private const string Url = "https://example.org/notice";
    private const string ValidFrom = "2026-01-01";

    private const string Notice =
        "SUMMARY: Employers in region A are subject to a surcharge of 0.8 percent for 2025. " +
        "The surcharge is due with the annual return.";

    private static Entry Entry(int? referenceYear, string quote = null) => new()
    {
        Key = "Surcharge.A",
        Content = "0.008",
        Unit = "percent",
        SourceQuote = quote,
        ReferenceYear = referenceYear,
        SourceUrls = [Url]
    };

    private static SourceResult Verify(Entry entry) =>
        SourceVerifier.Verify(entry,
            [(Url, new SourceDocument { State = SourceState.Readable, Sections = [("SUMMARY:", Notice)] })],
            validFrom: ValidFrom);

    // source verification -----------------------------------------------------

    private const string Quote = "Employers in region A are subject to a surcharge of 0.8 percent for 2025";

    [Fact]
    public void QuoteOfOtherYear_WithoutReferenceYear_IsAmbiguous()
    {
        var result = Verify(Entry(null, Quote));

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal(SourceVerifier.MultiYearAmbiguity, result.Ambiguity);
    }

    [Fact]
    public void QuoteOfReferenceYear_IsFound()
    {
        var result = Verify(Entry(2025, Quote));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.True(result.CanWrite);
    }

    private static SourceMatch VerifyBundle(int? referenceYear, string text) =>
        SourceVerifier.Verify(new Entry
            {
                Key = "Surcharge.A",
                Content = """{"Rate":0.008,"WageBase":8000}""",
                ReferenceYear = referenceYear,
                SourceUrls = [Url]
            },
            [(Url, new SourceDocument { State = SourceState.Readable, Sections = [("Page 1", text)] })],
            validFrom: ValidFrom).Match;

    private const string Text2025 = "Surcharge for 2025: 0.8 percent of the first $8,000 of wages.";

    [Fact]
    public void ValueOfOtherYear_WithoutReferenceYear_IsAmbiguous() =>
        Assert.Equal(SourceMatch.Ambiguous, VerifyBundle(null, Text2025));

    [Fact]
    public void ReferenceYear_ReplacesValidFromYear() =>
        Assert.Equal(SourceMatch.Found, VerifyBundle(2025, Text2025));

    [Fact]
    public void ReferenceYear_ValueOfValidFromYear_IsAmbiguous() =>
        Assert.Equal(SourceMatch.Ambiguous,
            VerifyBundle(2025, "Surcharge for 2026: 0.8 percent of the first $8,000 of wages."));

    // validation, check --------------------------------------------------------

    [Theory]
    [InlineData(25)]
    [InlineData(2101)]
    public void InvalidReferenceYear_IsReported(int year)
    {
        using var s = new TestSatellite();
        var errors = Validator.Validate(TestSatellite.Manifest(Entry(year)), s.LexDirectory);

        Assert.Contains(errors, e => e.Contains($"referenceYear {year} is not a year"));
    }

    [Fact]
    public void Check_OtherReferenceYear_IsInformation()
    {
        using var s = new TestSatellite();
        s.WriteRegulation("""[ { "name": "Surcharge", "values": [ { "key": "A", "value": "0.008" } ] } ]""");
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");
        var entry = Entry(2025);
        var manifest = TestSatellite.Manifest(new Entry
        {
            Key = entry.Key, Content = entry.Content, Unit = entry.Unit, ReferenceYear = 2025, SourceUrls = ["https://example.org/doc"],
            SourceLocation = "p. 1", LegalReference = "Act §1"
        }, new Entry
        {
            Key = "Surcharge.B", Content = "0.045", ReferenceYear = 2026, SourceUrls = ["https://example.org/doc"],
            SourceLocation = "p. 1", LegalReference = "Act §1"
        });
        manifest.Exclusions.Add(new Exclusion { Kind = Exclusion.KindOrphan, Pattern = "Surcharge.B", Reason = "test" });

        var result = Checker.Check(manifest, s.LexPath, release: true);

        Assert.Equal(1, result.OtherReferenceYear);
        Assert.Equal(["Surcharge.A (2025)"], result.OtherReferenceYearSamples);
        Assert.Contains("| Entries with a reference year other than the validFrom year | 1 |", CheckReport.BuildMarkdown([result]));
    }

    // snapshot, editor ---------------------------------------------------------

    [Fact]
    public async Task Snapshot_HoldsReferenceYear()
    {
        using var s = new TestSatellite();
        var plain = await s.WriteSnapshot(TestSatellite.Manifest(Entry(null)));
        var snapshot = await s.WriteSnapshot(TestSatellite.Manifest(Entry(2025)));

        Assert.NotEqual(plain.SourceHash, snapshot.SourceHash);
        Assert.Equal(2025, Assert.Single(snapshot.Values).ReferenceYear);
        Assert.Contains(Verifier.Verify(TestSatellite.Manifest(Entry(2024)), snapshot, s.LexDirectory),
            e => e.Contains("referenceYear differs from the snapshot"));
        Assert.DoesNotContain(Verifier.Verify(TestSatellite.Manifest(Entry(2025)), snapshot, s.LexDirectory),
            e => e.Contains("referenceYear"));
    }

    [Fact]
    public void Editor_AddValue_WritesReferenceYear()
    {
        var editor = ManifestEditor.Parse("{ \"values\": [] }");

        editor.AddValue(Entry(2025));

        Assert.Contains("\"referenceYear\": 2025", editor.ToJson());
    }

    [Fact]
    public void Rollover_OpenEntries_KeepsReferenceYear()
    {
        var editor = ManifestEditor.Parse(
            "{ \"values\": [ { \"key\": \"Surcharge.A\", \"content\": \"0.008\", \"referenceYear\": 2025, " +
            "\"verification\": { \"mode\": \"manual\", \"verifiedOn\": \"2026-11-06\" } } ] }");

        editor.OpenEntries();

        var json = editor.ToJson();
        Assert.Contains("\"referenceYear\": 2025", json);
        Assert.DoesNotContain("verification", json);
    }
}
