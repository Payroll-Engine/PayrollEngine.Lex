using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ReleaseGateTests
{
    private const string DocUrl = "https://example.org/doc";
    private const string SummaryUrl = "https://summary.example.com/rates";

    private static readonly string[] Keys = ["A", "B", "C", "D", "E"];

    private static string Lookup(params (string Key, string Value)[] values) =>
        "[ { \"name\": \"Rates\", \"values\": [ " +
        string.Join(", ", values.Select(x => $"{{ \"key\": \"{x.Key}\", \"value\": \"{x.Value}\" }}")) + " ] } ]";

    private static Verification Auto() => SourceVerifier.AutoVerification(new DateOnly(2026, 11, 3), "rates.html");

    private static Verification LiveAuto() => SourceVerifier.AutoVerification(new DateOnly(2026, 11, 3), null);

    private static Verification Manual() => new()
    {
        Mode = Verification.ModeManual, VerifiedOn = "2026-11-03", VerifiedBy = "Jane Doe", Evidence = "rates.html"
    };

    private static Entry Value(string key, string content, Verification verification = null, string location = "p. 1",
        List<string> urls = null) => new()
    {
        Key = key,
        Content = content,
        Unit = "%",
        SourceUrls = urls ?? [DocUrl],
        SourceLocation = location,
        LegalReference = "Test Act s. 1",
        DocumentRef = "rates.html",
        Verification = verification
    };

    private static Manifest Manifest(params Entry[] values)
    {
        var manifest = TestSatellite.Manifest(values);
        manifest.Sources.Add(new Source { Url = SummaryUrl, Description = "Commercial summary", Official = false });
        manifest.Files.Add(new EvidenceFile { FileName = "rates.html" });
        return manifest;
    }

    private static TestSatellite Satellite(params (string Key, string Value)[] values)
    {
        var s = new TestSatellite();
        s.WriteRegulation(Lookup(values));
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{ \"files\": [ { \"fileName\": \"rates.html\" } ] }");
        File.WriteAllText(Path.Combine(s.LexDirectory, "rates.html"),
            "<html><body><h2>Rates</h2><p>Rate A is 7.5% and rate B is 10%; rate C is 12.75%.</p><h2>Notes</h2><p>" +
            string.Concat(Enumerable.Repeat("filler text ", 60)) + "</p></body></html>");
        return s;
    }

    [Fact]
    public async Task Release_AmbiguousAutoVerifiedEntry_NeedsManualVerification()
    {
        using var s = Satellite(("A", "7.5"), ("B", "10"), ("C", "12.75"));
        var manifest = Manifest(Value("Rates.A", "7.5", Auto()), Value("Rates.B", "10", Manual()), Value("Rates.C", "12.75", Auto()));

        var plain = Checker.Check(manifest, s.LexPath);
        var release = Checker.Check(manifest, s.LexPath, release: true);
        await Checker.CheckSources(manifest, release);

        Assert.False(plain.HasFindings);
        Assert.True(release.RequireLocation);
        Assert.True(release.RequireVerification);
        Assert.True(release.SourcesVerified);
        Assert.Equal(1, release.Ambiguous);
        Assert.Equal(["Rates.A (ambiguous: short number)"], release.ManualVerificationSamples);
        Assert.True(release.HasFindings);
        Assert.Contains("Manual verification required (1)", CheckReport.BuildMarkdown([release]));
    }

    private static Entry WithoutEvidence(string key, string content, Verification verification) => new()
    {
        Key = key, Content = content, Unit = "%", SourceUrls = [DocUrl], SourceLocation = "p. 1",
        LegalReference = "Test Act s. 1", Verification = verification
    };

    private static void WriteLiveSources(TestSatellite s, string textHash) =>
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"),
            "{ \"liveSources\": [ { \"url\": \"" + DocUrl + "\", " +
            (textHash != null ? $"\"textHash\": \"{textHash}\"" : "\"reason\": \"HTTP 403\"") + " } ] }");

    [Fact]
    public async Task Release_NoLocalEvidenceAndNotReadableLive_IsNotVerifiable()
    {
        using var s = Satellite(("C", "12.75"));
        WriteLiveSources(s, null);
        var manifest = Manifest(WithoutEvidence("Rates.C", "12.75", LiveAuto()));
        manifest.Files.Clear();
        File.Delete(Path.Combine(s.LexDirectory, "rates.html"));

        var plain = Checker.Check(manifest, s.LexPath);
        var release = Checker.Check(manifest, s.LexPath, release: true);
        await Checker.CheckSources(manifest, release);

        Assert.False(plain.HasFindings);
        Assert.Equal(1, release.NotVerifiable);
        Assert.Equal(["Rates.C (no local evidence)"], release.NotVerifiableSamples);
        Assert.True(release.HasFindings);
        Assert.Contains("Verifiable neither locally nor live (1)", CheckReport.BuildMarkdown([release]));
    }

    [Fact]
    public async Task Release_NoLocalEvidenceWithoutLiveState_IsNotVerifiable()
    {
        using var s = Satellite(("C", "12.75"));
        var manifest = Manifest(WithoutEvidence("Rates.C", "12.75", LiveAuto()));
        manifest.Files.Clear();
        File.Delete(Path.Combine(s.LexDirectory, "rates.html"));
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "snapshot.json"), "{}");

        var release = Checker.Check(manifest, s.LexPath, release: true);
        await Checker.CheckSources(manifest, release);

        Assert.Equal(1, release.NotVerifiable);
    }

    [Fact]
    public async Task Release_NoLocalEvidenceButReadableLive_IsVerifiable()
    {
        using var s = Satellite(("C", "12.75"));
        WriteLiveSources(s, "sha256:abc");
        var manifest = Manifest(WithoutEvidence("Rates.C", "12.75", LiveAuto()));
        manifest.Files.Clear();
        File.Delete(Path.Combine(s.LexDirectory, "rates.html"));

        var release = Checker.Check(manifest, s.LexPath, release: true);
        await Checker.CheckSources(manifest, release);

        Assert.Equal(0, release.NotVerifiable);
    }

    [Fact]
    public async Task Release_NoLocalEvidenceWithManualVerification_IsVerifiable()
    {
        using var s = Satellite(("C", "12.75"));
        WriteLiveSources(s, null);
        var manifest = Manifest(WithoutEvidence("Rates.C", "12.75", Manual()));

        var release = Checker.Check(manifest, s.LexPath, release: true);
        await Checker.CheckSources(manifest, release);

        Assert.Equal(0, release.NotVerifiable);
    }

    [Fact]
    public async Task Release_QuoteNotFound_NeedsManualVerification()
    {
        using var s = Satellite(("C", "12.75"));
        var entry = Value("Rates.C", "12.75", Auto());
        var manifest = Manifest(new Entry
        {
            Key = entry.Key, Content = entry.Content, Unit = entry.Unit, SourceUrls = entry.SourceUrls,
            SourceLocation = entry.SourceLocation, LegalReference = entry.LegalReference, DocumentRef = entry.DocumentRef,
            Verification = entry.Verification, SourceQuote = "rate C is 13%"
        });

        var result = Checker.Check(manifest, s.LexPath, release: true);
        await Checker.CheckSources(manifest, result);

        Assert.Equal(1, result.QuoteNotFound);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void SecondaryMainSource_IsFindingOnlyInRelease()
    {
        using var s = Satellite(("A", "7.5"), ("B", "10"));
        var manifest = Manifest(
            Value("Rates.A", "7.5", Manual(), urls: [SummaryUrl, DocUrl]),
            Value("Rates.B", "10", Manual(), urls: [DocUrl, SummaryUrl]));

        var plain = Checker.Check(manifest, s.LexPath);
        var release = Checker.Check(manifest, s.LexPath, release: true);

        Assert.Equal(1, plain.SecondaryMainSource);
        Assert.Equal([$"Rates.A: {SummaryUrl}"], plain.SecondaryMainSourceSamples);
        Assert.False(plain.HasFindings);
        Assert.True(release.HasFindings);
        Assert.Contains("Secondary main source (1)", CheckReport.BuildMarkdown([release]));
    }

    [Fact]
    public void SharedLongLocation_IsNotEntrySpecific()
    {
        var values = Keys.Select(x => (x, "12.75")).ToArray();
        using var s = Satellite(values);
        var location = "Pages 1-12: " + new string('x', 200);
        var manifest = Manifest(Keys.Select(x => Value($"Rates.{x}", "12.75", Manual(), location)).ToArray());

        var optional = Checker.Check(manifest, s.LexPath);
        var required = Checker.Check(manifest, s.LexPath, requireLocation: true);

        Assert.Equal(5, optional.UnspecificLocations);
        Assert.False(optional.HasFindings);
        Assert.True(required.HasFindings);
    }

    [Fact]
    public void SharedShortLocation_IsEntrySpecific()
    {
        using var s = Satellite(Keys.Select(x => (x, "12.75")).ToArray());
        var manifest = Manifest(Keys.Select(x => Value($"Rates.{x}", "12.75", Manual(), "Table 3")).ToArray());

        Assert.Equal(0, Checker.Check(manifest, s.LexPath, requireLocation: true).UnspecificLocations);
    }

    [Fact]
    public void OverlongLocation_IsNotEntrySpecific()
    {
        using var s = Satellite(("A", "7.5"));
        var manifest = Manifest(Value("Rates.A", "7.5", Manual(), new string('x', 501)));

        Assert.Equal(1, Checker.Check(manifest, s.LexPath).UnspecificLocations);
    }

    [Fact]
    public void AutoVerificationExclusion_WithAutoRecord_IsFindingInRelease()
    {
        using var s = Satellite(("C", "12.75"));
        var manifest = Manifest(Value("Rates.C", "12.75", Auto()));
        manifest.Exclusions.Add(new Exclusion { Kind = Exclusion.KindAutoVerification, Pattern = "Rates.C", Reason = "verified by payroll expert" });

        var plain = Checker.Check(manifest, s.LexPath);
        var release = Checker.Check(manifest, s.LexPath, release: true);

        Assert.False(plain.HasFindings);
        Assert.Equal(1, release.ExcludedAutoVerified);
        Assert.True(release.HasFindings);
        Assert.Contains("autoVerification `Rates.C`: verified by payroll expert", plain.ExclusionNotes);
        Assert.Contains("1 entries excluded from auto verification", CheckReport.BuildMarkdown([plain]));
    }

    [Fact]
    public void UnusedAutoVerificationExclusion_IsFinding()
    {
        using var s = Satellite(("C", "12.75"));
        var manifest = Manifest(Value("Rates.C", "12.75", Manual()));
        manifest.Exclusions.Add(new Exclusion { Kind = Exclusion.KindAutoVerification, Pattern = "Other", Reason = "test" });

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Contains(result.ValidationErrors, x => x.Contains("(autoVerification) 'Other' matches no entry"));
        Assert.True(result.HasFindings);
    }
}
