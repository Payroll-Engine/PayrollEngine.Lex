using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ValidatorTests
{
    private const string EvidenceFile = "evidence.pdf";

    private static Verification Auto() => new()
    {
        Mode = Verification.ModeAuto,
        VerifiedOn = "2026-01-15",
        Method = "LexVerifySource 1.2"
    };

    private static Verification Manual() => new()
    {
        Mode = Verification.ModeManual,
        VerifiedOn = "2026-01-15",
        VerifiedBy = "Jane Doe",
        Evidence = EvidenceFile
    };

    private static List<string> Validate(Verification verification, string review = null)
    {
        using var s = new TestSatellite();
        File.WriteAllText(Path.Combine(s.LexDirectory, EvidenceFile), "evidence");
        var value = TestSatellite.Value("Rates.A", "7.5");
        var manifest = TestSatellite.Manifest(new Entry
        {
            Key = value.Key,
            Content = value.Content,
            SourceUrls = value.SourceUrls,
            SourceLocation = value.SourceLocation,
            LegalReference = value.LegalReference,
            Verification = verification
        });
        manifest.Sources[0] = new Source { Url = "https://example.org/doc", Review = review };
        manifest.Files.Add(new EvidenceFile { FileName = EvidenceFile });
        return Validator.Validate(manifest, s.LexDirectory);
    }

    [Fact]
    public void WithoutVerification_IsValid() =>
        Assert.Empty(Validate(null));

    [Fact]
    public void AutoVerification_IsValid() =>
        Assert.Empty(Validate(Auto()));

    [Fact]
    public void ManualVerification_IsValid() =>
        Assert.Empty(Validate(Manual()));

    [Fact]
    public void ManualVerificationWithReview_IsValid()
    {
        var verification = Manual();
        Assert.Empty(Validate(new Verification
        {
            Mode = verification.Mode,
            VerifiedOn = verification.VerifiedOn,
            VerifiedBy = verification.VerifiedBy,
            Evidence = verification.Evidence,
            ReviewedBy = "John Doe",
            ReviewedOn = "2026-01-16"
        }, Source.ReviewFourEyes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SingleAuthor")]
    [InlineData("FourEyes")]
    public void KnownReview_IsValid(string review) =>
        Assert.Empty(Validate(null, review));

    [Fact]
    public void UnknownReview_IsReported() =>
        Assert.Contains(Validate(null, "Peer"), e => e.Contains("review must be"));

    [Fact]
    public void UnknownMode_IsReported() =>
        Assert.Contains(Validate(new Verification { Mode = "estimated", VerifiedOn = "2026-01-15" }),
            e => e.Contains("mode must be"));

    [Theory]
    [InlineData(null, "missing required field: verifiedOn")]
    [InlineData("15.01.2026", "invalid verifiedOn format")]
    public void InvalidVerifiedOn_IsReported(string verifiedOn, string expected) =>
        Assert.Contains(Validate(new Verification
        {
            Mode = Verification.ModeAuto,
            VerifiedOn = verifiedOn,
            Method = "LexVerifySource 1.2"
        }), e => e.Contains(expected));

    [Fact]
    public void ManualWithoutVerifiedByAndEvidence_IsReported()
    {
        var errors = Validate(new Verification { Mode = Verification.ModeManual, VerifiedOn = "2026-01-15" });
        Assert.Contains(errors, e => e.Contains("missing required field: verifiedBy"));
        Assert.Contains(errors, e => e.Contains("missing required field: evidence"));
    }

    [Fact]
    public void ManualWithMethod_IsReported()
    {
        var verification = Manual();
        Assert.Contains(Validate(new Verification
        {
            Mode = verification.Mode,
            VerifiedOn = verification.VerifiedOn,
            VerifiedBy = verification.VerifiedBy,
            Evidence = verification.Evidence,
            Method = "LexVerifySource 1.2"
        }), e => e.Contains("method is only allowed"));
    }

    [Fact]
    public void AutoWithoutMethod_IsReported() =>
        Assert.Contains(Validate(new Verification { Mode = Verification.ModeAuto, VerifiedOn = "2026-01-15" }),
            e => e.Contains("missing required field: method"));

    [Fact]
    public void AutoWithVerifiedBy_IsReported() =>
        Assert.Contains(Validate(new Verification
        {
            Mode = Verification.ModeAuto,
            VerifiedOn = "2026-01-15",
            Method = "LexVerifySource 1.2",
            VerifiedBy = "Jane Doe"
        }), e => e.Contains("verifiedBy is only allowed"));

    [Fact]
    public void UndeclaredEvidence_IsReported() =>
        Assert.Contains(Validate(new Verification
        {
            Mode = Verification.ModeManual,
            VerifiedOn = "2026-01-15",
            VerifiedBy = "Jane Doe",
            Evidence = "other.pdf"
        }), e => e.Contains("evidence 'other.pdf' not found in files[]"));

    [Fact]
    public void ReviewedByWithoutReviewedOn_IsReported()
    {
        var verification = Manual();
        Assert.Contains(Validate(new Verification
        {
            Mode = verification.Mode,
            VerifiedOn = verification.VerifiedOn,
            VerifiedBy = verification.VerifiedBy,
            Evidence = verification.Evidence,
            ReviewedBy = "John Doe"
        }), e => e.Contains("must be set together"));
    }

    private static List<string> ValidateManifest(Action<Manifest> change)
    {
        using var s = new TestSatellite();
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"));
        change(manifest);
        return Validator.Validate(manifest, s.LexDirectory);
    }

    [Fact]
    public void AutoVerificationExclusion_IsValid() =>
        Assert.Empty(ValidateManifest(m => m.Exclusions.Add(
            new Exclusion { Kind = Exclusion.KindAutoVerification, Pattern = "Rates.A", Reason = "verified by payroll expert" })));

    [Fact]
    public void UnknownExclusionKind_NamesAutoVerification() =>
        Assert.Contains(ValidateManifest(m => m.Exclusions.Add(new Exclusion { Kind = "manual", Pattern = "Rates.A", Reason = "x" })),
            e => e.Contains("'autoVerification'"));

    [Fact]
    public void EmptySourceQuote_IsReported() =>
        Assert.Contains(ValidateManifest(m => m.Values[0] = new Entry
        {
            Key = "Rates.A", Content = "7.5", SourceUrls = ["https://example.org/doc"], SourceQuote = " "
        }), e => e.Contains("sourceQuote is empty"));

    [Fact]
    public void SecondarySource_IsValid() =>
        Assert.Empty(ValidateManifest(m => m.Sources.Add(new Source { Url = "https://summary.example.com", Official = false })));

    [Fact]
    public void SourceOfficialAndSecondary_IsReported() =>
        Assert.Contains(ValidateManifest(m => m.Sources.Add(new Source { Url = "https://example.org/doc", Official = false })),
            e => e.Contains("declared as official and as secondary source"));

    [Fact]
    public void InvalidReviewedOn_IsReported()
    {
        var verification = Manual();
        Assert.Contains(Validate(new Verification
        {
            Mode = verification.Mode,
            VerifiedOn = verification.VerifiedOn,
            VerifiedBy = verification.VerifiedBy,
            Evidence = verification.Evidence,
            ReviewedBy = "John Doe",
            ReviewedOn = "tomorrow"
        }), e => e.Contains("invalid reviewedOn format"));
    }
}
