using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ManifestEditorTests
{
    private const string Json = """
        {
          // comment
          "authority": "XX",
          "custom": { "keep": true },
          "values": [
            { "key": "Rates.A", "content": "8.5", "sourceUrls": ["https://example.org/doc"], "note": "kept" },
            { "key": "Rates.B", "content": "10", "sourceLocation": "p. 2",
              "verification": { "mode": "manual", "verifiedOn": "2026-01-10", "verifiedBy": "Jane Doe", "evidence": "doc.pdf" } },
            { "key": "Rates.C", "content": "12",
              "verification": { "mode": "auto", "verifiedOn": "2026-01-10", "method": "LexVerifySource 1.2.0", "evidence": "doc.html" } },
          ]
        }
        """;

    private static Verification Auto(string evidence, string date = "2026-02-01") =>
        SourceVerifier.AutoVerification(DateOnly.Parse(date), evidence);

    private static JsonElement Entry(ManifestEditor editor, string key)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.GetProperty("values").EnumerateArray()
            .Single(x => x.GetProperty("key").GetString() == key).Clone();
    }

    [Fact]
    public void SourceLocation_IsWrittenOnlyWhenMissing()
    {
        var editor = ManifestEditor.Parse(Json);
        Assert.True(editor.SetSourceLocation("Rates.A", "Section \"Rates\""));
        Assert.False(editor.SetSourceLocation("Rates.B", "Section \"Other\""));
        Assert.False(editor.SetSourceLocation("Unknown", "x"));
        Assert.Equal("Section \"Rates\"", Entry(editor, "Rates.A").GetProperty("sourceLocation").GetString());
        Assert.Equal("p. 2", Entry(editor, "Rates.B").GetProperty("sourceLocation").GetString());
        Assert.Equal(1, editor.Changes);
    }

    [Fact]
    public void AutoVerification_IsWritten_AndKeepsOtherProperties()
    {
        var editor = ManifestEditor.Parse(Json);
        Assert.True(editor.SetAutoVerification("Rates.A", Auto("doc.html")));

        var entry = Entry(editor, "Rates.A");
        var verification = entry.GetProperty("verification");
        Assert.Equal("auto", verification.GetProperty("mode").GetString());
        Assert.Equal("2026-02-01", verification.GetProperty("verifiedOn").GetString());
        Assert.Equal(SourceVerifier.Method, verification.GetProperty("method").GetString());
        Assert.Equal("doc.html", verification.GetProperty("evidence").GetString());
        Assert.False(verification.TryGetProperty("verifiedBy", out _));
        Assert.Equal("kept", entry.GetProperty("note").GetString());
        Assert.Contains("\"custom\"", editor.ToJson());
    }

    [Fact]
    public void ManualVerification_IsNeverReplaced()
    {
        var editor = ManifestEditor.Parse(Json);
        Assert.False(editor.SetAutoVerification("Rates.B", Auto("doc.html")));
        Assert.Equal("manual", Entry(editor, "Rates.B").GetProperty("verification").GetProperty("mode").GetString());
    }

    [Fact]
    public void AutoVerification_SameMethodAndEvidence_KeepsDate()
    {
        var editor = ManifestEditor.Parse(Json.Replace("LexVerifySource 1.2.0", SourceVerifier.Method));
        Assert.False(editor.SetAutoVerification("Rates.C", Auto("doc.html")));
        Assert.Equal("2026-01-10", Entry(editor, "Rates.C").GetProperty("verification").GetProperty("verifiedOn").GetString());
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void AutoVerification_OtherEvidence_IsReplaced()
    {
        var editor = ManifestEditor.Parse(Json.Replace("LexVerifySource 1.2.0", SourceVerifier.Method));
        Assert.True(editor.SetAutoVerification("Rates.C", Auto(null)));
        var verification = Entry(editor, "Rates.C").GetProperty("verification");
        Assert.Equal("2026-02-01", verification.GetProperty("verifiedOn").GetString());
        Assert.False(verification.TryGetProperty("evidence", out _));
    }

    [Fact]
    public void ManualRecord_IsRejected() =>
        Assert.Throws<ArgumentException>(() => ManifestEditor.Parse(Json).SetAutoVerification("Rates.A",
            new Verification { Mode = Verification.ModeManual, VerifiedOn = "2026-01-01" }));

    [Fact]
    public void WrittenRecord_IsValid()
    {
        using var s = new TestSatellite();
        File.WriteAllText(Path.Combine(s.LexDirectory, "doc.html"), "evidence");
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "8.5"));
        manifest.Files.Add(new EvidenceFile { FileName = "doc.html" });
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var editor = ManifestEditor.Parse(json);
        Assert.True(editor.SetAutoVerification("Rates.A", Auto("doc.html")));

        var edited = JsonSerializer.Deserialize<Manifest>(editor.ToJson(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Empty(Validator.Validate(edited!, s.LexDirectory));
    }
}
