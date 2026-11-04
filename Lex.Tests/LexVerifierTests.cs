using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class LexVerifierTests
{
    private static LexManifest ManifestWithFile() => new()
    {
        Authority = "XX",
        Regulation = "Data.Test",
        ValidFrom = "2026-01-01",
        Provider = "Test Authority",
        Sources = [new LexSource { Url = "https://example.org/doc", Description = "Test source" }],
        Values =
        [
            TestSatellite.Value("Rates.A", "7.5"),
            new LexValue
            {
                Key = "Table.All",
                ContentFile = "table.json",
                SourceUrl = "https://example.org/doc",
                SourceLocation = "p. 2",
                LegalReference = "Test Act s. 2"
            }
        ]
    };

    private static async Task<(TestSatellite Satellite, LexManifest Manifest, LexSnapshot Snapshot)> Prepare()
    {
        var s = new TestSatellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "table.json"), "{\"a\":1}");
        var manifest = ManifestWithFile();
        var snapshot = await s.WriteSnapshot(manifest);
        return (s, manifest, snapshot);
    }

    [Fact]
    public async Task FreshSnapshot_IsVerified()
    {
        var (s, manifest, snapshot) = await Prepare();
        using (s)
        {
            Assert.Empty(LexVerifier.Verify(manifest, snapshot, s.LexDirectory));
        }
    }

    [Fact]
    public async Task ChangedInlineContent_IsReported()
    {
        var (s, _, snapshot) = await Prepare();
        using (s)
        {
            var changed = ManifestWithFile();
            changed.Values[0] = TestSatellite.Value("Rates.A", "8.0");
            Assert.NotEmpty(LexVerifier.Verify(changed, snapshot, s.LexDirectory));
        }
    }

    [Fact]
    public async Task ChangedEvidenceFile_IsReported()
    {
        var (s, manifest, snapshot) = await Prepare();
        using (s)
        {
            await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "table.json"), "{\"a\":2}");
            Assert.NotEmpty(LexVerifier.Verify(manifest, snapshot, s.LexDirectory));
        }
    }

    [Fact]
    public async Task MissingEvidenceFile_IsReported()
    {
        var (s, manifest, snapshot) = await Prepare();
        using (s)
        {
            File.Delete(Path.Combine(s.LexDirectory, "table.json"));
            Assert.NotEmpty(LexVerifier.Verify(manifest, snapshot, s.LexDirectory));
        }
    }

    [Fact]
    public async Task AddedManifestValue_IsReported()
    {
        var (s, _, snapshot) = await Prepare();
        using (s)
        {
            var extended = ManifestWithFile();
            extended.Values.Add(TestSatellite.Value("Rates.New", "1"));
            Assert.Contains(LexVerifier.Verify(extended, snapshot, s.LexDirectory), e => e.Contains("Rates.New"));
        }
    }

    [Fact]
    public async Task SnapshotRoundTrip_FromJson_IsVerified()
    {
        var (s, manifest, _) = await Prepare();
        using (s)
        {
            var json = await File.ReadAllTextAsync(Path.Combine(s.LexDirectory, "snapshot.json"));
            var snapshot = JsonSerializer.Deserialize<LexSnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Empty(LexVerifier.Verify(manifest, snapshot, s.LexDirectory));
        }
    }
}
