using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class OpenEntryTests
{
    private const string OpenLex = """
        {
          "validFrom": "2027-01-01",
          "values": [
            { "key": "Rates.A", "content": "8.5", "open": true }
          ],
          "files": [ { "fileName": "evidence.pdf" } ]
        }
        """;

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static Entry OpenValue(string key, string content)
    {
        var value = TestSatellite.Value(key, content);
        return new Entry
        {
            Key = value.Key,
            Content = value.Content,
            SourceUrls = value.SourceUrls,
            SourceLocation = value.SourceLocation,
            LegalReference = value.LegalReference,
            Open = true
        };
    }

    private static Entry ReadEntry(ManifestEditor editor) =>
        JsonSerializer.Deserialize<Manifest>(editor.ToJson(), ReadOptions)!.Values[0];

    [Fact]
    public void ManualVerification_RemovesTheOpenFlag()
    {
        var editor = ManifestEditor.Parse(OpenLex);

        editor.SetManualVerification("Rates.A",
            new Verification { Mode = Verification.ModeManual, VerifiedOn = "2027-01-05", VerifiedBy = "Tester", Evidence = "evidence.pdf" },
            overwrite: false);

        var entry = ReadEntry(editor);
        Assert.False(entry.Open);
        Assert.Equal(Verification.ModeManual, entry.Verification.Mode);
        Assert.DoesNotContain("\"open\"", editor.ToJson());
    }

    [Fact]
    public void AutoVerification_RemovesTheOpenFlag()
    {
        var editor = ManifestEditor.Parse(OpenLex);

        editor.SetAutoVerification("Rates.A",
            new Verification { Mode = Verification.ModeAuto, VerifiedOn = "2027-01-05", Method = "LexVerifySource 1.0", Evidence = "evidence.pdf" });

        Assert.False(ReadEntry(editor).Open);
    }

    [Fact]
    public void OpenEntryWithVerification_IsInvalid()
    {
        using var s = new TestSatellite();
        var value = OpenValue("Rates.A", "8.5");
        var manifest = TestSatellite.Manifest(new Entry
        {
            Key = value.Key,
            Content = value.Content,
            SourceUrls = value.SourceUrls,
            SourceLocation = value.SourceLocation,
            LegalReference = value.LegalReference,
            Open = true,
            Verification = new Verification { Mode = Verification.ModeAuto, VerifiedOn = "2027-01-05", Method = "LexVerifySource 1.0" }
        });

        Assert.Contains(Validator.Validate(manifest, s.LexDirectory), x => x.Contains("open entry"));
    }

    [Fact]
    public void ClosedEntry_IsNotWrittenAsOpen()
    {
        var json = JsonSerializer.Serialize(TestSatellite.Value("Rates.A", "8.5"));

        Assert.DoesNotContain("Open", json);
    }

    [Fact]
    public async Task OpenFlag_IsPartOfTheSnapshot()
    {
        using var s = new TestSatellite();
        var open = TestSatellite.Manifest(OpenValue("Rates.A", "8.5"));
        var snapshot = await s.WriteSnapshot(open);

        Assert.Empty(Verifier.Verify(open, snapshot, s.LexDirectory));
        Assert.Contains(Verifier.Verify(TestSatellite.Manifest(TestSatellite.Value("Rates.A", "8.5")), snapshot, s.LexDirectory),
            x => x.Contains("sourceHash"));
    }

    [Fact]
    public async Task OpenEntry_IsAFindingWithoutRequiredMode()
    {
        using var s = new TestSatellite();
        s.WriteRegulation("""[ { "name": "Rates", "values": [ { "key": "A", "value": "8.5" } ] } ]""");
        var manifest = TestSatellite.Manifest(OpenValue("Rates.A", "8.5"));
        await s.WriteSnapshot(manifest);

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Equal(1, result.Open);
        Assert.Equal(0, result.Unverified);
        Assert.Equal(["Rates.A"], result.OpenSamples);
        Assert.True(result.HasFindings);
    }
}
