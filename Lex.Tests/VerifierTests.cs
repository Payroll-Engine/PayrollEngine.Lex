using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class VerifierTests
{
    private static Manifest ManifestWithFile() => new()
    {
        Authority = "XX",
        Regulation = "Data.Test",
        ValidFrom = "2026-01-01",
        Provider = "Test Authority",
        Sources = [new Source { Url = "https://example.org/doc", Description = "Test source" }],
        Values =
        [
            TestSatellite.Value("Rates.A", "7.5"),
            new Entry
            {
                Key = "Table.All",
                ContentFile = "table.json",
                SourceUrls = ["https://example.org/doc"],
                SourceLocation = "p. 2",
                LegalReference = "Test Act s. 2"
            }
        ]
    };

    private static async Task<(TestSatellite Satellite, Manifest Manifest, Snapshot Snapshot)> Prepare()
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
            Assert.Empty(Verifier.Verify(manifest, snapshot, s.LexDirectory));
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
            Assert.NotEmpty(Verifier.Verify(changed, snapshot, s.LexDirectory));
        }
    }

    [Fact]
    public async Task ChangedEvidenceFile_IsReported()
    {
        var (s, manifest, snapshot) = await Prepare();
        using (s)
        {
            await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "table.json"), "{\"a\":2}");
            Assert.NotEmpty(Verifier.Verify(manifest, snapshot, s.LexDirectory));
        }
    }

    [Fact]
    public async Task MissingEvidenceFile_IsReported()
    {
        var (s, manifest, snapshot) = await Prepare();
        using (s)
        {
            File.Delete(Path.Combine(s.LexDirectory, "table.json"));
            Assert.NotEmpty(Verifier.Verify(manifest, snapshot, s.LexDirectory));
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
            Assert.Contains(Verifier.Verify(extended, snapshot, s.LexDirectory), e => e.Contains("Rates.New"));
        }
    }

    private static Manifest WithQuote(Manifest manifest, string quote)
    {
        var value = manifest.Values[0];
        manifest.Values[0] = new Entry
        {
            Key = value.Key, Content = value.Content, SourceUrls = value.SourceUrls, SourceLocation = value.SourceLocation,
            LegalReference = value.LegalReference, SourceQuote = quote
        };
        return manifest;
    }

    [Fact]
    public async Task AddedSourceQuote_IsReported()
    {
        var (s, _, snapshot) = await Prepare();
        using (s)
        {
            var errors = Verifier.Verify(WithQuote(ManifestWithFile(), "the rate is 7.5%"), snapshot, s.LexDirectory);
            Assert.Contains(errors, e => e.Contains("sourceQuote differs"));
            Assert.Contains(errors, e => e.StartsWith("sourceHash differs", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task SnapshotWithSourceQuote_IsVerified()
    {
        using var s = new TestSatellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "table.json"), "{\"a\":1}");
        var manifest = WithQuote(ManifestWithFile(), "the rate is 7.5%");

        var snapshot = await s.WriteSnapshot(manifest);

        Assert.Equal("the rate is 7.5%", snapshot.Values[0].SourceQuote);
        Assert.Empty(Verifier.Verify(manifest, snapshot, s.LexDirectory));
    }

    private const string EvidenceFile = "evidence.pdf";

    private static Manifest ManifestWithVerification(string verifiedBy = "Jane Doe")
    {
        var manifest = ManifestWithFile();
        var value = manifest.Values[0];
        manifest.Values[0] = new Entry
        {
            Key = value.Key,
            Content = value.Content,
            SourceUrls = value.SourceUrls,
            SourceLocation = value.SourceLocation,
            LegalReference = value.LegalReference,
            Verification = new Verification
            {
                Mode = Verification.ModeManual,
                VerifiedOn = "2026-01-15",
                VerifiedBy = verifiedBy,
                Evidence = EvidenceFile
            }
        };
        manifest.Files.Add(new EvidenceFile { FileName = EvidenceFile });
        return manifest;
    }

    private static async Task<(TestSatellite Satellite, Manifest Manifest, Snapshot Snapshot)> PrepareVerification()
    {
        var s = new TestSatellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "table.json"), "{\"a\":1}");
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, EvidenceFile), "evidence");
        var manifest = ManifestWithVerification();
        var snapshot = await s.WriteSnapshot(manifest);
        return (s, manifest, snapshot);
    }

    [Fact]
    public async Task SourceHash_WithoutVerification_IsUnchanged()
    {
        using var s = new TestSatellite();
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"), TestSatellite.Value("Rates.B", "1200"));
        var snapshot = await s.WriteSnapshot(manifest);
        Assert.Equal("sha256:" + ContentHash.StringHash("7.51200"), snapshot.SourceHash);
    }

    [Fact]
    public async Task SourceHash_IncludesVerification()
    {
        var (s, manifest, snapshot) = await PrepareVerification();
        using (s)
        {
            var other = await new SnapshotWriter().WriteAsync(ManifestWithVerification("John Doe"),
                s.LexDirectory, s.LexDirectory, dryRun: true);
            Assert.NotEqual(snapshot.SourceHash, other.SourceHash);
            Assert.Empty(Verifier.Verify(manifest, snapshot, s.LexDirectory));
        }
    }

    [Fact]
    public async Task VerificationRoundTrip_FromJson_IsVerified()
    {
        var (s, manifest, _) = await PrepareVerification();
        using (s)
        {
            var json = await File.ReadAllTextAsync(Path.Combine(s.LexDirectory, "snapshot.json"));
            Assert.Contains("\"verification\"", json);
            var snapshot = JsonSerializer.Deserialize<Snapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Equal("Jane Doe", snapshot!.Values.Single(v => v.Key == "Rates.A").Verification.VerifiedBy);
            Assert.Empty(Verifier.Verify(manifest, snapshot, s.LexDirectory));
        }
    }

    [Fact]
    public async Task SnapshotWithoutVerification_HasNoVerificationProperty()
    {
        var (s, _, _) = await Prepare();
        using (s)
        {
            var json = await File.ReadAllTextAsync(Path.Combine(s.LexDirectory, "snapshot.json"));
            Assert.DoesNotContain("verification", json);
            Assert.DoesNotContain("review", json);
        }
    }

    [Fact]
    public async Task ChangedVerification_IsReported()
    {
        var (s, _, snapshot) = await PrepareVerification();
        using (s)
        {
            var errors = Verifier.Verify(ManifestWithVerification("John Doe"), snapshot, s.LexDirectory);
            Assert.Contains(errors, e => e.Contains("verification differs"));
            Assert.Contains(errors, e => e.Contains("sourceHash differs"));
        }
    }

    [Fact]
    public async Task AddedVerification_IsReported()
    {
        var (s, _, snapshot) = await Prepare();
        using (s)
        {
            await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, EvidenceFile), "evidence");
            Assert.Contains(Verifier.Verify(ManifestWithVerification(), snapshot, s.LexDirectory),
                e => e.Contains("verification differs"));
        }
    }

    [Fact]
    public async Task RemovedVerification_IsReported()
    {
        var (s, _, snapshot) = await PrepareVerification();
        using (s)
        {
            var manifest = ManifestWithFile();
            manifest.Files.Add(new EvidenceFile { FileName = EvidenceFile });
            Assert.Contains(Verifier.Verify(manifest, snapshot, s.LexDirectory),
                e => e.Contains("verification is in the snapshot but not in lex.json"));
        }
    }

    [Fact]
    public async Task ChangedVerificationEvidence_IsReported()
    {
        var (s, manifest, snapshot) = await PrepareVerification();
        using (s)
        {
            await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, EvidenceFile), "changed");
            Assert.Contains(Verifier.Verify(manifest, snapshot, s.LexDirectory),
                e => e.Contains($"File '{EvidenceFile}': content differs"));
        }
    }

    [Fact]
    public async Task VerificationEvidenceMissingInSnapshot_IsReported()
    {
        var (s, manifest, snapshot) = await PrepareVerification();
        using (s)
        {
            snapshot.Files.RemoveAll(f => f.FileName == EvidenceFile);
            Assert.Contains(Verifier.Verify(manifest, snapshot, s.LexDirectory),
                e => e.Contains($"verification evidence '{EvidenceFile}' is not in the snapshot"));
        }
    }

    [Fact]
    public async Task SnapshotRoundTrip_FromJson_IsVerified()
    {
        var (s, manifest, _) = await Prepare();
        using (s)
        {
            var json = await File.ReadAllTextAsync(Path.Combine(s.LexDirectory, "snapshot.json"));
            var snapshot = JsonSerializer.Deserialize<Snapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Empty(Verifier.Verify(manifest, snapshot, s.LexDirectory));
        }
    }
}
