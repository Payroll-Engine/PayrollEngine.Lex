using Xunit;

namespace PayrollEngine.Lex.Tests;

public class CheckerVerificationTests
{
    private const string ThreeValues =
        "[ { \"name\": \"Rates\", \"values\": [ { \"key\": \"A\", \"value\": \"7.5\" }, " +
        "{ \"key\": \"B\", \"value\": \"10\" }, { \"key\": \"C\", \"value\": \"12\" } ] } ]";

    private static Verification Auto() => new()
    {
        Mode = Verification.ModeAuto,
        VerifiedOn = "2026-11-03",
        Method = "LexVerifySource 1.2.0"
    };

    private static Verification Manual(string evidence = "rates.pdf", string reviewedBy = null, string reviewedOn = null) => new()
    {
        Mode = Verification.ModeManual,
        VerifiedOn = "2026-11-03",
        VerifiedBy = "Jane Doe",
        Evidence = evidence,
        ReviewedBy = reviewedBy,
        ReviewedOn = reviewedOn
    };

    private static Entry Value(string key, string content, Verification verification) => new()
    {
        Key = key,
        Content = content,
        SourceUrls = ["https://example.org/doc"],
        SourceLocation = "p. 1",
        LegalReference = "Test Act s. 1",
        Verification = verification
    };

    private static Manifest Manifest(string review, params Entry[] values) => new()
    {
        Authority = "XX",
        Regulation = "Data.Test",
        ValidFrom = "2026-01-01",
        Provider = "Test Authority",
        Sources = [new Source { Url = "https://example.org/doc", Description = "Test source", Review = review }],
        Values = [.. values],
        Files = [new EvidenceFile { FileName = "rates.pdf", Description = "Rates" }]
    };

    private static TestSatellite Satellite(string snapshotJson = "{ \"files\": [ { \"fileName\": \"rates.pdf\" } ] }")
    {
        var s = new TestSatellite();
        s.WriteRegulation(ThreeValues);
        File.WriteAllText(Path.Combine(s.LexDirectory, "rates.pdf"), "rates");
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), snapshotJson);
        return s;
    }

    [Fact]
    public void Verification_IsCountedPerMode()
    {
        using var s = Satellite();
        var manifest = Manifest(null,
            Value("Rates.A", "7.5", Auto()), Value("Rates.B", "10", Manual()), Value("Rates.C", "12", null));

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Equal(1, result.VerifiedAuto);
        Assert.Equal(1, result.VerifiedManual);
        Assert.Equal(1, result.Unverified);
        Assert.Contains("Rates.C", result.UnverifiedSamples);
    }

    [Fact]
    public void Unverified_IsFindingOnlyWhenRequired()
    {
        using var s = Satellite();
        var manifest = Manifest(null,
            Value("Rates.A", "7.5", Auto()), Value("Rates.B", "10", Auto()), Value("Rates.C", "12", null));

        var optional = Checker.Check(manifest, s.LexPath);
        var required = Checker.Check(manifest, s.LexPath, requireVerification: true);

        Assert.False(optional.HasFindings);
        Assert.True(required.HasFindings);
        Assert.Equal(1, required.Unverified);
    }

    [Fact]
    public void Verification_AllEntriesVerified_HasNoFindingWhenRequired()
    {
        using var s = Satellite();
        var manifest = Manifest(null,
            Value("Rates.A", "7.5", Auto()), Value("Rates.B", "10", Manual()), Value("Rates.C", "12", Auto()));

        var result = Checker.Check(manifest, s.LexPath, requireVerification: true);

        Assert.False(result.HasFindings);
    }

    [Fact]
    public void FourEyes_ManualWithoutReview_IsFinding()
    {
        using var s = Satellite();
        var manifest = Manifest(Source.ReviewFourEyes,
            Value("Rates.A", "7.5", Auto()), Value("Rates.B", "10", Manual()), Value("Rates.C", "12", Auto()));

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Equal(1, result.MissingReview);
        Assert.Contains("Rates.B", result.MissingReviewSamples);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void FourEyes_ManualWithReview_HasNoFinding()
    {
        using var s = Satellite();
        var manifest = Manifest(Source.ReviewFourEyes,
            Value("Rates.A", "7.5", Auto()), Value("Rates.B", "10", Manual(reviewedBy: "John Doe", reviewedOn: "2026-11-04")),
            Value("Rates.C", "12", Auto()));

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Equal(0, result.MissingReview);
        Assert.False(result.HasFindings);
    }

    [Fact]
    public void SingleAuthor_ManualWithoutReview_HasNoFinding()
    {
        using var s = Satellite();
        var manifest = Manifest(Source.ReviewSingleAuthor,
            Value("Rates.A", "7.5", Manual()), Value("Rates.B", "10", Manual()), Value("Rates.C", "12", Manual()));

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Equal(0, result.MissingReview);
    }

    [Fact]
    public void VerificationEvidence_NotInSnapshot_IsFinding()
    {
        using var s = Satellite("{ \"files\": [] }");
        var manifest = Manifest(null,
            Value("Rates.A", "7.5", Auto()), Value("Rates.B", "10", Manual()), Value("Rates.C", "12", Auto()));

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Contains("verification evidence not in the snapshot: rates.pdf", result.FileProblems);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void VerificationEvidence_InSnapshot_HasNoProblem()
    {
        using var s = Satellite();
        var manifest = Manifest(null,
            Value("Rates.A", "7.5", Auto()), Value("Rates.B", "10", Manual()), Value("Rates.C", "12", Auto()));

        var result = Checker.Check(manifest, s.LexPath);

        Assert.DoesNotContain(result.FileProblems, x => x.StartsWith("verification evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void Report_ContainsVerificationStatistics()
    {
        using var s = Satellite();
        var manifest = Manifest(null,
            Value("Rates.A", "7.5", Auto()), Value("Rates.B", "10", Manual()), Value("Rates.C", "12", null));
        var results = new List<CheckResult> { Checker.Check(manifest, s.LexPath) };

        var markdown = CheckReport.BuildMarkdown(results);
        var csv = CheckReport.BuildCsv(results);

        Assert.Contains("| Entries without verification | 1 |", markdown);
        Assert.Contains("| Data.Test.2026 | 3 | 1 | 1 | 1 | 0 | 0 |", markdown);
        Assert.EndsWith(";VerifiedAuto;VerifiedManual;Unverified;MissingReview;Open;UnspecificLocations;" +
                        "SecondaryMainSource;Release;Ambiguous;QuoteNotFound;ExcludedAutoVerified;ExcludedDerived", csv.Split('\n')[0].TrimEnd('\r'));
        Assert.EndsWith(";1;1;1;0;0;0;0;False;0;0;0;0", csv.Split('\n')[1].TrimEnd('\r'));
    }
}
