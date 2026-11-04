using Xunit;

namespace PayrollEngine.Lex.Tests;

public class LexValidatorTests
{
    private const string EvidenceFile = "evidence.pdf";

    private static LexVerification Auto() => new()
    {
        Mode = LexVerification.ModeAuto,
        VerifiedOn = "2026-01-15",
        Method = "LexVerifySource 1.2"
    };

    private static LexVerification Manual() => new()
    {
        Mode = LexVerification.ModeManual,
        VerifiedOn = "2026-01-15",
        VerifiedBy = "Jane Doe",
        Evidence = EvidenceFile
    };

    private static List<string> Validate(LexVerification verification, string review = null)
    {
        using var s = new TestSatellite();
        File.WriteAllText(Path.Combine(s.LexDirectory, EvidenceFile), "evidence");
        var value = TestSatellite.Value("Rates.A", "7.5");
        var manifest = TestSatellite.Manifest(new LexValue
        {
            Key = value.Key,
            Content = value.Content,
            SourceUrl = value.SourceUrl,
            SourceLocation = value.SourceLocation,
            LegalReference = value.LegalReference,
            Verification = verification
        });
        manifest.Sources[0] = new LexSource { Url = "https://example.org/doc", Review = review };
        manifest.Files.Add(new LexFile { FileName = EvidenceFile });
        return LexValidator.Validate(manifest, s.LexDirectory);
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
        Assert.Empty(Validate(new LexVerification
        {
            Mode = verification.Mode,
            VerifiedOn = verification.VerifiedOn,
            VerifiedBy = verification.VerifiedBy,
            Evidence = verification.Evidence,
            ReviewedBy = "John Doe",
            ReviewedOn = "2026-01-16"
        }, LexSource.ReviewFourEyes));
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
        Assert.Contains(Validate(new LexVerification { Mode = "estimated", VerifiedOn = "2026-01-15" }),
            e => e.Contains("mode must be"));

    [Theory]
    [InlineData(null, "missing required field: verifiedOn")]
    [InlineData("15.01.2026", "invalid verifiedOn format")]
    public void InvalidVerifiedOn_IsReported(string verifiedOn, string expected) =>
        Assert.Contains(Validate(new LexVerification
        {
            Mode = LexVerification.ModeAuto,
            VerifiedOn = verifiedOn,
            Method = "LexVerifySource 1.2"
        }), e => e.Contains(expected));

    [Fact]
    public void ManualWithoutVerifiedByAndEvidence_IsReported()
    {
        var errors = Validate(new LexVerification { Mode = LexVerification.ModeManual, VerifiedOn = "2026-01-15" });
        Assert.Contains(errors, e => e.Contains("missing required field: verifiedBy"));
        Assert.Contains(errors, e => e.Contains("missing required field: evidence"));
    }

    [Fact]
    public void ManualWithMethod_IsReported()
    {
        var verification = Manual();
        Assert.Contains(Validate(new LexVerification
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
        Assert.Contains(Validate(new LexVerification { Mode = LexVerification.ModeAuto, VerifiedOn = "2026-01-15" }),
            e => e.Contains("missing required field: method"));

    [Fact]
    public void AutoWithVerifiedBy_IsReported() =>
        Assert.Contains(Validate(new LexVerification
        {
            Mode = LexVerification.ModeAuto,
            VerifiedOn = "2026-01-15",
            Method = "LexVerifySource 1.2",
            VerifiedBy = "Jane Doe"
        }), e => e.Contains("verifiedBy is only allowed"));

    [Fact]
    public void UndeclaredEvidence_IsReported() =>
        Assert.Contains(Validate(new LexVerification
        {
            Mode = LexVerification.ModeManual,
            VerifiedOn = "2026-01-15",
            VerifiedBy = "Jane Doe",
            Evidence = "other.pdf"
        }), e => e.Contains("evidence 'other.pdf' not found in files[]"));

    [Fact]
    public void ReviewedByWithoutReviewedOn_IsReported()
    {
        var verification = Manual();
        Assert.Contains(Validate(new LexVerification
        {
            Mode = verification.Mode,
            VerifiedOn = verification.VerifiedOn,
            VerifiedBy = verification.VerifiedBy,
            Evidence = verification.Evidence,
            ReviewedBy = "John Doe"
        }), e => e.Contains("must be set together"));
    }

    [Fact]
    public void InvalidReviewedOn_IsReported()
    {
        var verification = Manual();
        Assert.Contains(Validate(new LexVerification
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
