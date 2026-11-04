using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ManifestEditorEntryTests
{
    private const string Json = """
        {
          "authority": "XX",
          "sources": [
            { "url": "https://example.org/doc", "description": "Doc" },
            { "url": "https://example.org/four", "description": "Four", "review": "FourEyes" }
          ],
          "values": [
            { "key": "Rates.A", "content": "8.5", "sourceUrl": "https://example.org/four" },
            { "key": "Rates.B", "content": "10",
              "verification": { "mode": "manual", "verifiedOn": "2026-01-10", "verifiedBy": "Jane Doe", "evidence": "doc.pdf" } },
            { "key": "Rates.C", "content": "12",
              "verification": { "mode": "auto", "verifiedOn": "2026-01-10", "method": "LexVerifySource 1.2.0", "evidence": "doc.html" } },
            { "key": "Limits.Max", "content": "100" }
          ],
          "files": [ { "fileName": "doc.pdf", "description": "Doc" } ]
        }
        """;

    private static Verification Manual(string verifiedBy = "John Doe", string evidence = "doc.pdf", string date = "2026-02-01") => new()
    {
        Mode = Verification.ModeManual,
        VerifiedOn = date,
        VerifiedBy = verifiedBy,
        Evidence = evidence
    };

    private static JsonElement Entry(ManifestEditor editor, string key)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.GetProperty("values").EnumerateArray()
            .Single(x => x.GetProperty("key").GetString() == key).Clone();
    }

    [Theory]
    [InlineData("Rates", new[] { "Rates.A", "Rates.B", "Rates.C" })]
    [InlineData("Rates.B", new[] { "Rates.B" })]
    [InlineData("Ra*", new[] { "Rates.A", "Rates.B", "Rates.C" })]
    [InlineData("Rate", new string[0])]
    public void MatchKeys_UsesKeyPattern(string pattern, string[] expected) =>
        Assert.Equal(expected, ManifestEditor.Parse(Json).MatchKeys(pattern));

    [Fact]
    public void Lookups_FindSourcesFilesAndReview()
    {
        var editor = ManifestEditor.Parse(Json);
        Assert.True(editor.HasKey("Rates.A"));
        Assert.True(editor.HasSource("https://example.org/doc"));
        Assert.False(editor.HasSource("https://example.org/other"));
        Assert.True(editor.HasFile("DOC.pdf"));
        Assert.Equal(Source.ReviewFourEyes, editor.SourceReview("Rates.A"));
        Assert.Null(editor.SourceReview("Rates.B"));
    }

    [Fact]
    public void AddValue_WritesMissingRequiredFieldsEmpty()
    {
        var editor = ManifestEditor.Parse(Json);

        editor.AddValue(new Entry { Key = "Rates.D", Content = "7", Unit = "rate" });

        var entry = Entry(editor, "Rates.D");
        Assert.Equal("7", entry.GetProperty("content").GetString());
        Assert.Equal("rate", entry.GetProperty("unit").GetString());
        Assert.Equal(string.Empty, entry.GetProperty("sourceUrl").GetString());
        Assert.Equal(string.Empty, entry.GetProperty("sourceLocation").GetString());
        Assert.Equal(string.Empty, entry.GetProperty("legalReference").GetString());
        Assert.False(entry.TryGetProperty("documentRef", out _));
        Assert.Equal(1, editor.Changes);
    }

    [Fact]
    public void AddValue_ExistingKey_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            ManifestEditor.Parse(Json).AddValue(new Entry { Key = "Rates.A" }));

    [Fact]
    public void AddValue_CreatesValuesArray()
    {
        var editor = ManifestEditor.Parse("{ \"authority\": \"XX\" }");
        editor.AddValue(new Entry { Key = "Rates.A", Content = "1" });
        Assert.Equal("1", Entry(editor, "Rates.A").GetProperty("content").GetString());
    }

    [Fact]
    public void AddFile_DeclaresOnce()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.True(editor.AddFile(new EvidenceFile { FileName = "rates.html", SourceUrl = "https://example.org/rates" }));
        Assert.False(editor.AddFile(new EvidenceFile { FileName = "RATES.html" }));
        Assert.Contains("\"sourceUrl\": \"https://example.org/rates\"", editor.ToJson());
        Assert.Equal(1, editor.Changes);
    }

    [Fact]
    public void SetManualVerification_ReplacesAutoAndKeepsManual()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(AttestResult.Written, editor.SetManualVerification("Rates.C", Manual(), false));
        Assert.Equal(AttestResult.ManualExists, editor.SetManualVerification("Rates.B", Manual(), false));
        Assert.Equal(AttestResult.NotFound, editor.SetManualVerification("Rates.Z", Manual(), false));

        var c = Entry(editor, "Rates.C").GetProperty("verification");
        Assert.Equal("manual", c.GetProperty("mode").GetString());
        Assert.Equal("John Doe", c.GetProperty("verifiedBy").GetString());
        Assert.False(c.TryGetProperty("method", out _));
        Assert.Equal("Jane Doe", Entry(editor, "Rates.B").GetProperty("verification").GetProperty("verifiedBy").GetString());
    }

    [Fact]
    public void SetManualVerification_SameRecord_IsUnchanged()
    {
        var editor = ManifestEditor.Parse(Json);
        Assert.Equal(AttestResult.Unchanged,
            editor.SetManualVerification("Rates.B", Manual("Jane Doe", "doc.pdf", "2026-01-10"), false));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void SetManualVerification_Overwrite_ReplacesManual()
    {
        var editor = ManifestEditor.Parse(Json);
        Assert.Equal(AttestResult.Written, editor.SetManualVerification("Rates.B", Manual(), true));
        Assert.Equal("John Doe", Entry(editor, "Rates.B").GetProperty("verification").GetProperty("verifiedBy").GetString());
    }

    [Fact]
    public void SetManualVerification_RequiresManualRecord() =>
        Assert.Throws<ArgumentException>(() => ManifestEditor.Parse(Json).SetManualVerification("Rates.A",
            new Verification { Mode = Verification.ModeAuto, VerifiedOn = "2026-02-01", Method = "x" }, false));

    [Fact]
    public void SetReview_OnlyOnManualRecord()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(AttestResult.Written, editor.SetReview("Rates.B", "John Doe", "2026-02-02", false));
        Assert.Equal(AttestResult.NoManualRecord, editor.SetReview("Rates.C", "John Doe", "2026-02-02", false));
        Assert.Equal(AttestResult.NoManualRecord, editor.SetReview("Rates.A", "John Doe", "2026-02-02", false));

        var b = Entry(editor, "Rates.B").GetProperty("verification");
        Assert.Equal("John Doe", b.GetProperty("reviewedBy").GetString());
        Assert.Equal("2026-02-02", b.GetProperty("reviewedOn").GetString());
        Assert.Equal("Jane Doe", b.GetProperty("verifiedBy").GetString());
    }

    [Fact]
    public void SetReview_ExistingReview_RequiresOverwrite()
    {
        var editor = ManifestEditor.Parse(Json);
        editor.SetReview("Rates.B", "John Doe", "2026-02-02", false);

        Assert.Equal(AttestResult.Unchanged, editor.SetReview("Rates.B", "John Doe", "2026-02-02", false));
        Assert.Equal(AttestResult.ReviewExists, editor.SetReview("Rates.B", "Max Doe", "2026-02-03", false));
        Assert.Equal(AttestResult.Written, editor.SetReview("Rates.B", "Max Doe", "2026-02-03", true));
    }

    [Fact]
    public void AttestedRecord_PassesValidation()
    {
        var editor = ManifestEditor.Parse(Json);
        editor.SetManualVerification("Rates.A", Manual(), false);
        editor.SetReview("Rates.A", "Jane Doe", "2026-02-02", false);

        var manifest = JsonSerializer.Deserialize<Manifest>(editor.ToJson(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var errors = Validator.Validate(manifest, Path.GetTempPath());

        Assert.DoesNotContain(errors, x => x.Contains("Rates.A", StringComparison.Ordinal));
    }
}
