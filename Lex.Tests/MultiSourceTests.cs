using Xunit;

namespace PayrollEngine.Lex.Tests;

public class MultiSourceTests
{
    private const string UnemploymentUrl = "https://www.unemployment.example.org/rates";
    private const string LeaveUrl = "https://leave.example.org/contributions";

    private static Entry Combined(string content = "{\"WageBase\":42000,\"LeaveRate\":0.0046}") => new()
    {
        Key = "Parameters.A",
        Content = content,
        Unit = "USD / rate",
        SourceUrls = [UnemploymentUrl, LeaveUrl]
    };

    private static SourceDocument Readable(string heading, string text) =>
        new() { State = SourceState.Readable, Sections = [(heading, text)] };

    private static SourceDocument Unreadable(string reason) =>
        new() { State = SourceState.Unreadable, Reason = reason };

    private static SourceDocument Missing() =>
        new() { State = SourceState.Missing, Reason = "no local evidence" };

    [Fact]
    public void NumbersSplitOverSources_AreFound()
    {
        var result = SourceVerifier.Verify(Combined(),
        [
            (UnemploymentUrl, Readable("Taxable wages", "Taxable wage base 42,000")),
            (LeaveUrl, Readable("Employee share", "The employee share is 0.46%"))
        ]);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal([UnemploymentUrl, LeaveUrl], result.Urls);
        Assert.Equal("unemployment.example.org: Section \"Taxable wages\"; leave.example.org: Section \"Employee share\"",
            result.Location);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void NumberMissingInAllSources_IsPartial()
    {
        var result = SourceVerifier.Verify(Combined(),
        [
            (UnemploymentUrl, Readable("Taxable wages", "Taxable wage base 42,000")),
            (LeaveUrl, Readable("Employee share", "The employee share is 0.5%"))
        ]);

        Assert.Equal(SourceMatch.Partial, result.Match);
        Assert.Equal(["0.0046"], result.Missing);
        Assert.Null(result.Location);
    }

    [Fact]
    public void UnreadableSource_IsNamedInReason()
    {
        var result = SourceVerifier.Verify(Combined(),
        [
            (UnemploymentUrl, Readable("Taxable wages", "Taxable wage base 42,000")),
            (LeaveUrl, Unreadable("HTTP 403"))
        ]);

        Assert.Equal(SourceMatch.Partial, result.Match);
        Assert.Equal("leave.example.org: HTTP 403", result.Reason);
    }

    [Fact]
    public void AllNumbersInReadableSource_FoundDespiteUnreadableSource()
    {
        var result = SourceVerifier.Verify(Combined(),
        [
            (UnemploymentUrl, Unreadable("HTTP 403")),
            (LeaveUrl, Readable("Rates", "Wage base 42,000, employee share 0.46%"))
        ]);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("leave.example.org: Section \"Rates\"", result.Location);
        Assert.Equal("unemployment.example.org: HTTP 403", result.Reason);
    }

    [Fact]
    public void NoReadableSource_IsUnreadable()
    {
        var result = SourceVerifier.Verify(Combined(),
            [(UnemploymentUrl, Unreadable("HTTP 403")), (LeaveUrl, Missing())]);

        Assert.Equal(SourceMatch.Unreadable, result.Match);
        Assert.Equal("unemployment.example.org: HTTP 403; leave.example.org: no local evidence", result.Reason);
    }

    [Fact]
    public void NoLocalEvidenceForAnySource_IsNoEvidence() =>
        Assert.Equal(SourceMatch.NoEvidence,
            SourceVerifier.Verify(Combined(), [(UnemploymentUrl, Missing()), (LeaveUrl, Missing())]).Match);

    [Fact]
    public void SingleSource_KeepsPlainLocationAndReason()
    {
        var entry = new Entry { Key = "Rates.A", Content = "8.55", SourceUrls = [LeaveUrl] };

        var found = SourceVerifier.Verify(entry, Readable("Rates", "Standard rate 8.55%"));
        var blocked = SourceVerifier.Verify(entry, Unreadable("HTTP 403"));

        Assert.Equal("Section \"Rates\"", found.Location);
        Assert.Equal([LeaveUrl], found.Urls);
        Assert.Equal("HTTP 403", blocked.Reason);
    }

    [Fact]
    public void Urls_SkipEmptyEntries()
    {
        var entry = new Entry { Key = "Rates.A", SourceUrls = [" ", UnemploymentUrl, ""] };

        Assert.Equal([UnemploymentUrl], entry.Urls);
        Assert.Empty(new Entry { Key = "Rates.B" }.Urls);
    }

    [Fact]
    public void LiveSourceUrls_ContainEveryEntryUrlOnce()
    {
        var manifest = TestSatellite.Manifest(Combined(),
            new Entry { Key = "Parameters.B", Content = "1", SourceUrls = [LeaveUrl] });

        var urls = LiveSource.Urls(manifest);

        Assert.Equal(["https://example.org/doc", UnemploymentUrl, LeaveUrl], urls);
    }

    [Fact]
    public void CheckedUrls_ContainFileUrls()
    {
        var manifest = TestSatellite.Manifest(Combined());
        manifest.Files.Add(new EvidenceFile { FileName = "rates.pdf", SourceUrls = ["https://files.example.org/rates.pdf"] });

        Assert.Contains("https://files.example.org/rates.pdf", UrlChecker.Urls(manifest));
        Assert.Contains(LeaveUrl, UrlChecker.Urls(manifest));
    }

    [Fact]
    public void EntryWithoutUrls_CountsAsMissingSourceUrls()
    {
        using var s = new TestSatellite();
        s.WriteRegulation("[ { \"name\": \"Rates\", \"values\": [ { \"key\": \"A\", \"value\": \"8.5\" } ] } ]");
        var manifest = TestSatellite.Manifest(new Entry
        {
            Key = "Rates.A", Content = "8.5", SourceUrls = [" "], SourceLocation = "p. 1", LegalReference = "Test Act s. 1"
        });

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Equal(1, result.MissingSourceUrls);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void FourEyesOnSecondSource_RequiresReview()
    {
        using var s = new TestSatellite();
        s.WriteRegulation("[ { \"name\": \"Rates\", \"values\": [ { \"key\": \"A\", \"value\": \"8.5\" } ] } ]");
        File.WriteAllText(Path.Combine(s.LexDirectory, "rates.pdf"), "rates");
        File.WriteAllText(Path.Combine(s.LexDirectory, "snapshot.json"), "{ \"files\": [ { \"fileName\": \"rates.pdf\" } ] }");
        var manifest = new Manifest
        {
            Authority = "XX",
            Regulation = "Data.Test",
            ValidFrom = "2026-01-01",
            Sources =
            [
                new Source { Url = UnemploymentUrl, Review = Source.ReviewSingleAuthor },
                new Source { Url = LeaveUrl, Review = Source.ReviewFourEyes }
            ],
            Values =
            [
                new Entry
                {
                    Key = "Rates.A", Content = "8.5", SourceUrls = [UnemploymentUrl, LeaveUrl], SourceLocation = "p. 1",
                    LegalReference = "Test Act s. 1",
                    Verification = new Verification
                    {
                        Mode = Verification.ModeManual, VerifiedOn = "2026-11-03", VerifiedBy = "Jane Doe", Evidence = "rates.pdf"
                    }
                }
            ],
            Files = [new EvidenceFile { FileName = "rates.pdf" }]
        };

        Assert.Equal(1, Checker.Check(manifest, s.LexPath).MissingReview);
    }

    [Fact]
    public void SourceReview_FourEyesWhenAnySourceRequiresIt()
    {
        var editor = ManifestEditor.Parse($$"""
            {
              "sources": [
                { "url": "{{UnemploymentUrl}}", "review": "SingleAuthor" },
                { "url": "{{LeaveUrl}}", "review": "FourEyes" }
              ],
              "values": [
                { "key": "Rates.A", "content": "8.5", "sourceUrls": ["{{UnemploymentUrl}}", "{{LeaveUrl}}"] },
                { "key": "Rates.B", "content": "10", "sourceUrls": ["{{UnemploymentUrl}}"] }
              ]
            }
            """);

        Assert.Equal(Source.ReviewFourEyes, editor.SourceReview("Rates.A"));
        Assert.Equal(Source.ReviewSingleAuthor, editor.SourceReview("Rates.B"));
    }

    [Fact]
    public void AddValue_WritesSourceUrls()
    {
        var editor = ManifestEditor.Parse("{ \"values\": [] }");

        editor.AddValue(new Entry { Key = "Rates.A", Content = "8.5", SourceUrls = [UnemploymentUrl, " ", LeaveUrl] });

        using var json = System.Text.Json.JsonDocument.Parse(editor.ToJson());
        var urls = json.RootElement.GetProperty("values")[0].GetProperty("sourceUrls");
        Assert.Equal([UnemploymentUrl, LeaveUrl], urls.EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void FileWithoutPathUrlsOrLocalCopy_IsInvalid()
    {
        using var s = new TestSatellite();
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "8.5"));
        manifest.Files.Add(new EvidenceFile { FileName = "absent.pdf", SourceUrls = [] });

        Assert.Contains(Validator.Validate(manifest, s.LexDirectory),
            x => x.Contains("absent.pdf", StringComparison.Ordinal) && x.Contains("no sourceUrls", StringComparison.Ordinal));
    }
}
