using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class AttestOperationTests
{
    private const string Json = """
        {
          "authority": "XX",
          "sources": [
            { "url": "https://example.org/rates", "review": "FourEyes" },
            { "url": "https://example.org/limits" }
          ],
          "values": [
            { "key": "Rates.A", "content": "8.5", "sourceUrls": [ "https://example.org/rates" ], "open": true },
            { "key": "Rates.B", "content": "9.5", "sourceUrls": [ "https://example.org/rates" ],
              "verification": { "mode": "auto", "verifiedOn": "2026-01-10", "method": "LexVerifySource 1.2.0", "evidence": "rates.pdf" } },
            { "key": "Limits.C", "content": "100", "sourceUrls": [ "https://example.org/limits" ],
              "verification": { "mode": "manual", "verifiedOn": "2026-01-10", "verifiedBy": "Jane Doe", "evidence": "rates.pdf" } }
          ],
          "files": [ { "fileName": "rates.pdf" }, { "fileName": "absent.pdf" } ]
        }
        """;

    private static TestSatellite Satellite()
    {
        var s = new TestSatellite();
        File.WriteAllText(s.LexPath, Json);
        File.WriteAllText(Path.Combine(s.LexDirectory, "rates.pdf"), "evidence");
        return s;
    }

    private static AttestRequest Verify(string key, string evidence = "rates.pdf", bool overwrite = false) => new()
    {
        Key = key, VerifiedBy = "John Doe", VerifiedOn = "2026-11-12", Evidence = evidence, Overwrite = overwrite
    };

    private static AttestRequest Review(string key, bool overwrite = false) => new()
    {
        Key = key, ReviewedBy = "Mary Major", ReviewedOn = "2026-11-12", Overwrite = overwrite
    };

    private static JsonElement Verification(TestSatellite s, string key)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(s.LexPath));
        return document.RootElement.GetProperty("values").EnumerateArray()
            .Single(x => x.GetProperty("key").GetString() == key).GetProperty("verification").Clone();
    }

    // parameter rules ----------------------------------------------------------

    [Theory]
    [InlineData(null, "Jane", null, "doc.pdf", null, null, "Missing entry key or key pattern")]
    [InlineData("Rates.A", "Jane", null, null, null, null, "Missing VerifiedBy and Evidence (or ReviewedBy for a review)")]
    [InlineData("Rates.A", null, null, "doc.pdf", null, null, "Missing VerifiedBy and Evidence (or ReviewedBy for a review)")]
    [InlineData("Rates.A", "Jane", "12.11.2026", "doc.pdf", null, null, "Invalid VerifiedOn '12.11.2026', expected yyyy-MM-dd")]
    [InlineData("Rates.A", "Jane", null, "doc.pdf", null, "2026-11-12", "ReviewedOn requires ReviewedBy")]
    [InlineData("Rates.A", "Jane", null, null, "John", null, "Verification and review are separate steps: use either VerifiedBy/Evidence or ReviewedBy")]
    [InlineData("Rates.A", null, "2026-11-12", null, "John", null, "Verification and review are separate steps: use either VerifiedBy/Evidence or ReviewedBy")]
    [InlineData("Rates.A", null, null, null, "John", "2026/11/12", "Invalid ReviewedOn '2026/11/12', expected yyyy-MM-dd")]
    public void InvalidParameters_AreRejected(string key, string verifiedBy, string verifiedOn, string evidence,
        string reviewedBy, string reviewedOn, string expected)
    {
        var request = new AttestRequest
        {
            Key = key, VerifiedBy = verifiedBy, VerifiedOn = verifiedOn, Evidence = evidence,
            ReviewedBy = reviewedBy, ReviewedOn = reviewedOn
        };
        Assert.Equal(expected, request.Test());
    }

    [Fact]
    public void ValidParameters_AreAccepted()
    {
        Assert.Null(new AttestRequest { Key = "Rates", VerifiedBy = "Jane", Evidence = "doc.pdf" }.Test());
        Assert.Null(new AttestRequest { Key = "Rates.*", ReviewedBy = "John" }.Test());
        Assert.True(new AttestRequest { Key = "Rates", ReviewedBy = "John" }.IsReview);
        Assert.False(new AttestRequest { Key = "Rates", ReviewedBy = " " }.IsReview);
    }

    [Fact]
    public async Task InvalidParameters_WriteNothing()
    {
        using var s = Satellite();

        var result = await AttestOperation.AttestAsync(s.LexPath, new AttestRequest { Key = "Rates.A", VerifiedBy = "Jane" });

        Assert.Equal("Missing VerifiedBy and Evidence (or ReviewedBy for a review)", result.Error);
        Assert.Equal(Json, await File.ReadAllTextAsync(s.LexPath));
    }

    // manual verification ------------------------------------------------------

    [Fact]
    public async Task Verification_ReplacesAutoRecordAndOpenFlag()
    {
        using var s = Satellite();

        var result = await AttestOperation.AttestAsync(s.LexDirectory, Verify("Rates"));

        Assert.Null(result.Error);
        Assert.Equal(s.LexPath, result.LexPath);
        Assert.Equal(["Rates.A", "Rates.B"], result.Entries.Select(x => x.Key));
        Assert.Equal(2, result.Written);
        Assert.Equal(0, result.Skipped);
        var verification = Verification(s, "Rates.B");
        Assert.Equal("manual", verification.GetProperty("mode").GetString());
        Assert.Equal("John Doe", verification.GetProperty("verifiedBy").GetString());
        Assert.DoesNotContain("\"open\"", await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task ManualRecord_IsSkippedWithoutOverwrite()
    {
        using var s = Satellite();

        var result = await AttestOperation.AttestAsync(s.LexPath, Verify("Limits.C"));

        Assert.Equal(AttestResult.ManualExists, Assert.Single(result.Entries).Result);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(Json, await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task ManualRecord_IsReplacedWithOverwrite()
    {
        using var s = Satellite();

        var result = await AttestOperation.AttestAsync(s.LexPath, Verify("Limits.C", overwrite: true));

        Assert.Equal(1, result.Written);
        Assert.Equal("John Doe", Verification(s, "Limits.C").GetProperty("verifiedBy").GetString());
    }

    [Fact]
    public async Task SameRecord_IsUnchanged()
    {
        using var s = Satellite();
        await AttestOperation.AttestAsync(s.LexPath, Verify("Rates.A"));
        var json = await File.ReadAllTextAsync(s.LexPath);

        var result = await AttestOperation.AttestAsync(s.LexPath, Verify("Rates.A"));

        Assert.Equal(AttestResult.Unchanged, Assert.Single(result.Entries).Result);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(json, await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task VerifiedOn_DefaultsToToday()
    {
        using var s = Satellite();

        await AttestOperation.AttestAsync(s.LexPath, new AttestRequest { Key = "Rates.A", VerifiedBy = "John Doe", Evidence = "rates.pdf" });

        Assert.Equal(EntryRules.Today(), Verification(s, "Rates.A").GetProperty("verifiedOn").GetString());
    }

    [Theory]
    [InlineData("Other", null, "No entry matches Other")]
    [InlineData("Rates.A", "unknown.pdf", "Evidence is not declared in files[] (LexFetch): unknown.pdf")]
    [InlineData("Rates.A", "absent.pdf", "Evidence file not found in the .lex folder: absent.pdf")]
    public async Task InvalidTarget_IsAnError(string key, string evidence, string expected)
    {
        using var s = Satellite();

        var result = await AttestOperation.AttestAsync(s.LexPath, Verify(key, evidence ?? "rates.pdf"));

        Assert.Equal(expected, result.Error);
        Assert.Empty(result.Entries);
        Assert.Equal(Json, await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task MissingManifest_IsAnError()
    {
        using var s = new TestSatellite();
        var folder = Path.Combine(s.Root, "Regulation");

        var result = await AttestOperation.AttestAsync(folder, Verify("Rates.A"));

        Assert.Null(result.LexPath);
        Assert.Equal($"lex.json not found: {folder}", result.Error);
    }

    // review -------------------------------------------------------------------

    [Fact]
    public async Task Review_OfManualRecord_IsWritten()
    {
        using var s = Satellite();

        var result = await AttestOperation.AttestAsync(s.LexPath, Review("Limits.C"));

        var entry = Assert.Single(result.Entries);
        Assert.Equal(AttestResult.Written, entry.Result);
        Assert.True(entry.ReviewNotRequired);
        var verification = Verification(s, "Limits.C");
        Assert.Equal("Mary Major", verification.GetProperty("reviewedBy").GetString());
        Assert.Equal("2026-11-12", verification.GetProperty("reviewedOn").GetString());
    }

    [Fact]
    public async Task Review_OfFourEyesSource_IsRequired()
    {
        using var s = Satellite();
        await AttestOperation.AttestAsync(s.LexPath, Verify("Rates.A"));

        var result = await AttestOperation.AttestAsync(s.LexPath, Review("Rates.A"));

        var entry = Assert.Single(result.Entries);
        Assert.Equal(AttestResult.Written, entry.Result);
        Assert.False(entry.ReviewNotRequired);
    }

    [Fact]
    public async Task Review_WithoutManualRecord_IsSkipped()
    {
        using var s = Satellite();

        var result = await AttestOperation.AttestAsync(s.LexPath, Review("Rates.*"));

        Assert.All(result.Entries, x => Assert.Equal(AttestResult.NoManualRecord, x.Result));
        Assert.All(result.Entries, x => Assert.False(x.ReviewNotRequired));
        Assert.Equal(2, result.Skipped);
        Assert.Equal(Json, await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task Review_ExistingReview_RequiresOverwrite()
    {
        using var s = Satellite();
        await AttestOperation.AttestAsync(s.LexPath, Review("Limits.C"));
        var other = new AttestRequest { Key = "Limits.C", ReviewedBy = "Richard Roe", ReviewedOn = "2026-11-13" };

        var skipped = await AttestOperation.AttestAsync(s.LexPath, other);
        var written = await AttestOperation.AttestAsync(s.LexPath, new AttestRequest
        {
            Key = other.Key, ReviewedBy = other.ReviewedBy, ReviewedOn = other.ReviewedOn, Overwrite = true
        });

        Assert.Equal(AttestResult.ReviewExists, Assert.Single(skipped.Entries).Result);
        Assert.Equal(AttestResult.Written, Assert.Single(written.Entries).Result);
        Assert.Equal("Richard Roe", Verification(s, "Limits.C").GetProperty("reviewedBy").GetString());
    }

    [Fact]
    public async Task Review_DoesNotRequireEvidenceFile()
    {
        using var s = Satellite();
        File.Delete(Path.Combine(s.LexDirectory, "rates.pdf"));

        var result = await AttestOperation.AttestAsync(s.LexPath, Review("Limits.C"));

        Assert.Null(result.Error);
        Assert.Equal(1, result.Written);
    }
}
