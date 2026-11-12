using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class VerifySourceOperationTests
{
    private const string Url = "https://example.org/limits";
    private static readonly DateOnly Today = new(2026, 11, 12);

    private const string EditorJson = """
        {
          "values": [
            { "key": "Rates.A", "content": "8.5" },
            { "key": "Rates.B", "content": "9", "sourceLocation": "Section \"Old\"" },
            { "key": "Rates.C", "content": "10", "sourceLocation": "Page 4, table 2" },
            { "key": "Rates.D", "content": "11",
              "verification": { "mode": "manual", "verifiedOn": "2026-01-10", "verifiedBy": "Jane Doe", "evidence": "doc.html" } },
            { "key": "Rates.E", "content": "12" },
            { "key": "Rates.F", "content": "13" },
            { "key": "Rates.G", "content": "14" },
            { "key": "Rates.H", "content": "15", "open": true }
          ]
        }
        """;

    private static SourceResult Result(string key, SourceMatch match = SourceMatch.Found,
        string location = "doc.html: Section \"Rates\"", string excluded = null) => new()
    {
        Key = key,
        Match = match,
        Location = location,
        Evidence = "doc.html",
        Languages = "en 1234abcd",
        ExcludedReason = excluded
    };

    private static JsonElement Entry(ManifestEditor editor, string key)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.GetProperty("values").EnumerateArray()
            .Single(x => x.GetProperty("key").GetString() == key).Clone();
    }

    private static string Property(ManifestEditor editor, string key, string name) =>
        Entry(editor, key).TryGetProperty(name, out var value) ? value.ToString() : null;

    private static string VerificationMode(ManifestEditor editor, string key) =>
        Entry(editor, key).TryGetProperty("verification", out var value) ? value.GetProperty("mode").GetString() : null;

    // write rules --------------------------------------------------------------

    [Fact]
    public void FoundEntry_GetsLocationAndAutoRecord()
    {
        var editor = ManifestEditor.Parse(EditorJson);

        var result = VerifySourceOperation.Write(editor, [Result("Rates.A")], refresh: false, Today);

        Assert.Equal(1, result.Locations);
        Assert.Equal(1, result.Verifications);
        Assert.Equal(2, result.Changes);
        Assert.Equal("doc.html: Section \"Rates\"", Property(editor, "Rates.A", "sourceLocation"));
        var verification = Entry(editor, "Rates.A").GetProperty("verification");
        Assert.Equal("auto", verification.GetProperty("mode").GetString());
        Assert.Equal("2026-11-12", verification.GetProperty("verifiedOn").GetString());
        Assert.Equal("doc.html", verification.GetProperty("evidence").GetString());
        Assert.Equal($"{SourceVerifier.Method}; languages en 1234abcd", verification.GetProperty("method").GetString());
    }

    [Theory]
    [InlineData(SourceMatch.Ambiguous)]
    [InlineData(SourceMatch.Partial)]
    [InlineData(SourceMatch.NotFound)]
    [InlineData(SourceMatch.QuoteNotFound)]
    [InlineData(SourceMatch.Unreadable)]
    [InlineData(SourceMatch.NoEvidence)]
    [InlineData(SourceMatch.NoNumbers)]
    [InlineData(SourceMatch.NoContent)]
    public void EntryNotFound_IsNotWritten(SourceMatch match)
    {
        var editor = ManifestEditor.Parse(EditorJson);

        var result = VerifySourceOperation.Write(editor, [Result("Rates.E", match)], refresh: false, Today);

        Assert.Equal(0, result.Changes);
        Assert.Equal(0, editor.Changes);
        Assert.Null(Property(editor, "Rates.E", "sourceLocation"));
        Assert.Null(VerificationMode(editor, "Rates.E"));
    }

    [Fact]
    public void ExcludedEntry_IsNotWritten()
    {
        var editor = ManifestEditor.Parse(EditorJson);

        var result = VerifySourceOperation.Write(editor, [Result("Rates.F", excluded: "verified by an expert")], refresh: false, Today);

        Assert.Equal(0, result.Changes);
        Assert.Null(VerificationMode(editor, "Rates.F"));
    }

    [Fact]
    public void ExistingLocations_AreKept()
    {
        var editor = ManifestEditor.Parse(EditorJson);

        var result = VerifySourceOperation.Write(editor, [Result("Rates.B"), Result("Rates.C")], refresh: false, Today);

        Assert.Equal(0, result.Locations);
        Assert.Equal(2, result.Verifications);
        Assert.Equal("Section \"Old\"", Property(editor, "Rates.B", "sourceLocation"));
        Assert.Equal("Page 4, table 2", Property(editor, "Rates.C", "sourceLocation"));
    }

    [Fact]
    public void Refresh_ReplacesToolLocationOnly()
    {
        var editor = ManifestEditor.Parse(EditorJson);

        var result = VerifySourceOperation.Write(editor, [Result("Rates.B"), Result("Rates.C")], refresh: true, Today);

        Assert.Equal(1, result.Locations);
        Assert.Equal("doc.html: Section \"Rates\"", Property(editor, "Rates.B", "sourceLocation"));
        Assert.Equal("Page 4, table 2", Property(editor, "Rates.C", "sourceLocation"));
    }

    [Fact]
    public void ManualRecord_IsNotReplaced()
    {
        var editor = ManifestEditor.Parse(EditorJson);

        var result = VerifySourceOperation.Write(editor, [Result("Rates.D")], refresh: false, Today);

        Assert.Equal(1, result.Locations);
        Assert.Equal(0, result.Verifications);
        Assert.Equal("manual", VerificationMode(editor, "Rates.D"));
    }

    [Fact]
    public void FoundEntry_LosesOpenFlag()
    {
        var editor = ManifestEditor.Parse(EditorJson);

        VerifySourceOperation.Write(editor, [Result("Rates.H")], refresh: false, Today);

        Assert.Null(Property(editor, "Rates.H", "open"));
        Assert.Equal("auto", VerificationMode(editor, "Rates.H"));
    }

    [Fact]
    public void FileIdentity_GetsFileIdentityRecord()
    {
        var editor = ManifestEditor.Parse(EditorJson);
        var table = new SourceResult
        {
            Key = "Rates.A", Match = SourceMatch.Found, FileIdentity = true, Evidence = "tariffs.zip", Languages = "en 1234abcd"
        };

        VerifySourceOperation.Write(editor, [table], refresh: false, Today);

        var verification = Entry(editor, "Rates.A").GetProperty("verification");
        Assert.Equal(ManifestVerifier.FileIdentityMethod, verification.GetProperty("method").GetString());
        Assert.Equal("tariffs.zip", verification.GetProperty("evidence").GetString());
        Assert.Null(Property(editor, "Rates.A", "sourceLocation"));
    }

    [Fact]
    public void DerivedProperties_AreListedInTheRecord()
    {
        var editor = ManifestEditor.Parse(EditorJson);
        var result = new SourceResult
        {
            Key = "Rates.A", Match = SourceMatch.Found, Evidence = "doc.html",
            Derived = [new DerivedValue { Property = "Monthly", Value = "700", Reason = "Annual / 12" }]
        };

        VerifySourceOperation.Write(editor, [result], refresh: false, Today);

        var derived = Entry(editor, "Rates.A").GetProperty("verification").GetProperty("derived");
        Assert.Equal(["Monthly"], derived.EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void RepeatedWrite_ChangesNothing()
    {
        var editor = ManifestEditor.Parse(EditorJson);
        VerifySourceOperation.Write(editor, [Result("Rates.A"), Result("Rates.B")], refresh: true, Today);
        var second = ManifestEditor.Parse(editor.ToJson());

        var result = VerifySourceOperation.Write(second, [Result("Rates.A"), Result("Rates.B")], refresh: true, Today.AddDays(1));

        Assert.Equal(0, result.Changes);
        Assert.Equal(0, second.Changes);
    }

    // vocabulary ---------------------------------------------------------------

    [Fact]
    public void Vocabulary_ArgumentBeforeVariable()
    {
        string asked = null;
        var vocabulary = VerifySourceOperation.ResolveVocabulary("C:\\Words", name =>
        {
            asked = name;
            return "C:\\Variable";
        });

        Assert.Equal("C:\\Words", vocabulary);
        Assert.Null(asked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Vocabulary_FromVariable(string argument)
    {
        string asked = null;
        var vocabulary = VerifySourceOperation.ResolveVocabulary(argument, name =>
        {
            asked = name;
            return "C:\\Variable";
        });

        Assert.Equal("C:\\Variable", vocabulary);
        Assert.Equal("PAYROLL_ENGINE_LEX_VOCABULARY", asked);
    }

    [Fact]
    public void Vocabulary_WithoutVariableReader_IsNull() =>
        Assert.Null(VerifySourceOperation.ResolveVocabulary(null, null));

    // manifest file ------------------------------------------------------------

    private const string ManifestJson = """
        {
          // comment
          "authority": "XX",
          "regulation": "Data.Test",
          "validFrom": "2026-01-01",
          "provider": "Test Authority",
          "languages": [ "en" ],
          "sources": [ { "url": "https://example.org/limits", "description": "Limits" } ],
          "values": [
            { "key": "Limits.Ceiling", "content": "84600", "sourceUrls": [ "https://example.org/limits" ],
              "sourceLocation": "", "legalReference": "Test Act s. 1" },
            { "key": "Limits.Floor", "content": "17345", "sourceUrls": [ "https://example.org/limits" ],
              "sourceLocation": "Page 1", "legalReference": "Test Act s. 2" },
          ],
          "files": [ { "fileName": "limits.html", "sourceUrls": [ "https://example.org/limits" ] } ]
        }
        """;

    // a source document needs a minimum of readable text
    private static readonly string Filler = string.Concat(Enumerable.Repeat(
        "The employer withholds the contribution from each payment and reports it with the periodic return. ", 5));

    private static TestSatellite Satellite()
    {
        var s = new TestSatellite();
        File.WriteAllText(s.LexPath, ManifestJson);
        File.WriteAllText(Path.Combine(s.LexDirectory, "limits.html"),
            "<html><body><h2>Limits 2026</h2><p>The annual contribution ceiling for 2026 is 84,600 for all employees " +
            "and applies to the wages paid in the calendar year.</p><p>" + Filler + "</p></body></html>");
        return s;
    }

    [Fact]
    public async Task VerifyFile_ReportsWithoutWriting()
    {
        using var s = Satellite();

        var file = await VerifySourceOperation.VerifyFileAsync(s.LexPath);

        Assert.Equal(s.LexPath, file.LexPath);
        Assert.Equal(2, file.Results.Count);
        Assert.Equal(1, file.Count(SourceMatch.Found));
        Assert.Equal(1, file.Count(SourceMatch.NotFound));
        Assert.Equal(1, file.Problems);
        Assert.Equal(0, file.Ambiguous);
        Assert.StartsWith("en ", file.Languages);
        Assert.Null(file.Write);
        Assert.Equal(ManifestJson, await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task ReportOnly_WritesNothing()
    {
        using var s = Satellite();
        var file = await VerifySourceOperation.VerifyFileAsync(s.LexPath);

        var write = await VerifySourceOperation.WriteAsync(file, SourceWriteMode.ReportOnly, Today);

        Assert.Null(write);
        Assert.Null(file.Write);
        Assert.Equal(ManifestJson, await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task WriteLocations_SavesFoundEntries_AndSecondRunChangesNothing()
    {
        using var s = Satellite();
        var file = await VerifySourceOperation.VerifyFileAsync(s.LexPath);

        var write = await VerifySourceOperation.WriteAsync(file, SourceWriteMode.WriteLocations, Today);

        Assert.Same(write, file.Write);
        Assert.True(write.Saved);
        Assert.Equal(1, write.Locations);
        Assert.Equal(1, write.Verifications);
        var json = await File.ReadAllTextAsync(s.LexPath);
        var editor = ManifestEditor.Parse(json);
        Assert.Equal("auto", VerificationMode(editor, "Limits.Ceiling"));
        Assert.True(SourceLocation.IsToolLocation(Property(editor, "Limits.Ceiling", "sourceLocation")));
        Assert.Null(VerificationMode(editor, "Limits.Floor"));
        Assert.Equal("Page 1", Property(editor, "Limits.Floor", "sourceLocation"));

        var again = await VerifySourceOperation.VerifyFileAsync(s.LexPath);
        var second = await VerifySourceOperation.WriteAsync(again, SourceWriteMode.RefreshLocations, Today);
        Assert.False(second.Saved);
        Assert.Equal(0, second.Changes);
        Assert.Equal(json, await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task UndeclaredEvidence_IsListed()
    {
        using var s = Satellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "other.html"), "<html></html>");

        var file = await VerifySourceOperation.VerifyFileAsync(s.LexPath);

        Assert.Contains("other.html (not in files[])", file.UnusedEvidence);
        Assert.DoesNotContain(file.UnusedEvidence, x => x.StartsWith("limits.html", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidJson_Throws()
    {
        using var s = new TestSatellite();
        await File.WriteAllTextAsync(s.LexPath, "{ \"values\": [ ");

        await Assert.ThrowsAnyAsync<JsonException>(() => VerifySourceOperation.VerifyFileAsync(s.LexPath));
    }
}
