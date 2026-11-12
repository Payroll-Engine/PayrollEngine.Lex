using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class WorkspaceLexSourceTests
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static Manifest Manifest() => new()
    {
        Authority = "XX",
        Regulation = "Data.Test",
        ValidFrom = "2026-01-01",
        Provider = "Test Authority",
        Languages = ["en"],
        Sources =
        [
            new Source { Url = "https://example.org/doc", Description = "Test source" },
            new Source { Url = "https://example.org/four", Description = "Four eyes", Review = Source.ReviewFourEyes }
        ],
        Values =
        [
            new Entry
            {
                Key = "Rates.A", Content = "7.5", SourceUrls = ["https://example.org/doc"], SourceLocation = "p. 1",
                LegalReference = "Test Act s. 1", SourceQuote = "rate 7.5",
                Verification = new Verification { Mode = Verification.ModeAuto, VerifiedOn = "2026-01-10", Method = "Test 1.0" }
            },
            new Entry
            {
                Key = "Rates.B", Content = "8.5", SourceUrls = ["https://example.org/four"], SourceLocation = "p. 2",
                LegalReference = "Test Act s. 2",
                Verification = new Verification
                {
                    Mode = Verification.ModeManual, VerifiedOn = "2026-01-10", VerifiedBy = "Jane Doe", Evidence = "doc.pdf"
                }
            },
            new Entry
            {
                Key = "Bundle.C", Content = "{ \"Rate\": 0, \"Total\": 5 }", SourceUrls = ["https://example.org/doc"],
                SourceLocation = "p. 3", LegalReference = "Test Act s. 3", Open = true
            }
        ],
        Files = [new EvidenceFile { FileName = "doc.pdf" }],
        Exclusions =
        [
            new Exclusion { Kind = Exclusion.KindNotApplicable, Pattern = "Bundle.C.Rate", Reason = "No such program" },
            new Exclusion { Kind = Exclusion.KindSourceLocation, Pattern = "Rates.*", Reason = "Table row" },
            new Exclusion { Kind = Exclusion.KindOrphan, Pattern = "Other.*", Reason = "Code" }
        ]
    };

    private static TestSatellite Satellite()
    {
        var s = new TestSatellite();
        File.WriteAllText(s.LexPath, JsonSerializer.Serialize(Manifest(), WriteOptions));
        File.WriteAllBytes(Path.Combine(s.LexDirectory, "doc.pdf"), [1, 2, 3]);
        return s;
    }

    private static async Task Snapshot(TestSatellite s)
    {
        var result = await SnapshotOperation.RunAsync(s.LexPath);
        Assert.Equal(SnapshotStatus.Created, result.Status);
    }

    [Fact]
    public async Task List_SummarizesManifest()
    {
        using var s = Satellite();
        var source = new WorkspaceLexSource([s.LexPath]);

        var list = await source.ListManifestsAsync();

        var info = Assert.Single(list);
        Assert.Equal(Path.GetFullPath(s.LexPath), info.Id);
        Assert.Equal("Data.Test.2026", info.Satellite);
        Assert.Equal("XX", info.Authority);
        Assert.Equal("Data.Test", info.Regulation);
        Assert.Equal("2026-01-01", info.ValidFrom);
        Assert.Equal(["en"], info.Languages);
        Assert.Equal(3, info.Entries);
        Assert.Equal(1, info.AutoVerified);
        Assert.Equal(1, info.ManualVerified);
        Assert.Equal(1, info.WithoutVerification);
        Assert.Equal(1, info.OpenEntries);
        Assert.Equal(2, info.Sources);
        Assert.Equal(1, info.Files);
        Assert.Equal(3, info.Exclusions);
        Assert.Null(info.Error);
    }

    [Fact]
    public async Task ContentHash_FollowsTheFile()
    {
        using var s = Satellite();
        var source = new WorkspaceLexSource([s.LexPath]);

        var first = (await source.GetManifestAsync(s.LexPath))!.ContentHash;
        Assert.Equal(ManifestHash.OfFile(s.LexPath), first);
        Assert.StartsWith(ManifestHash.Prefix, first);

        var editor = ManifestEditor.Parse(await File.ReadAllTextAsync(s.LexPath));
        editor.UpdateEntry("Rates.A", new EntryUpdate { Content = "9" });
        await editor.SaveAsync(s.LexPath);

        var second = (await source.GetManifestAsync(s.LexPath))!.ContentHash;
        Assert.NotEqual(first, second);
        Assert.Equal(ManifestHash.OfText(editor.ToJson()), second);
    }

    [Fact]
    public void ManifestHash_IsStableAndTextAndBytesAgree()
    {
        Assert.Equal(ManifestHash.OfText("{ }"), ManifestHash.OfText("{ }"));
        Assert.NotEqual(ManifestHash.OfText("{ }"), ManifestHash.OfText("{  }"));
        Assert.Equal(ManifestHash.OfText("abc"), ManifestHash.OfBytes("abc"u8.ToArray()));
        // sha256 of "abc"
        // ReSharper disable once StringLiteralTypo
        Assert.Equal("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ManifestHash.OfText("abc"));
    }

    [Fact]
    public async Task Snapshot_MissingCurrentStale()
    {
        using var s = Satellite();
        var source = new WorkspaceLexSource([s.LexPath]);

        var missing = await source.GetManifestAsync(s.LexPath);
        Assert.Equal(LexSnapshotState.Missing, missing!.SnapshotState);

        await Snapshot(s);
        var current = await source.GetManifestAsync(s.LexPath);
        Assert.Equal(LexSnapshotState.Current, current!.SnapshotState);
        Assert.Empty(current.SnapshotDifferences);

        var editor = ManifestEditor.Parse(await File.ReadAllTextAsync(s.LexPath));
        editor.UpdateEntry("Rates.A", new EntryUpdate { Content = "9" });
        await editor.SaveAsync(s.LexPath);
        var stale = await source.GetManifestAsync(s.LexPath);
        Assert.Equal(LexSnapshotState.Stale, stale!.SnapshotState);
        Assert.Contains(stale.SnapshotDifferences, x => x.Contains("Rates.A", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Snapshot_Unreadable()
    {
        using var s = Satellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, "snapshot.json"), "{ broken");
        var source = new WorkspaceLexSource([s.LexPath]);

        var info = await source.GetManifestAsync(s.LexPath);

        Assert.Equal(LexSnapshotState.Unreadable, info!.SnapshotState);
        Assert.NotEmpty(info.SnapshotDifferences);
        var entry = await source.GetEntryAsync(s.LexPath, "Rates.A");
        Assert.Equal(LexEntrySnapshotState.NoSnapshot, entry!.SnapshotState);
    }

    [Fact]
    public async Task Entry_CarriesSourcesReviewAndVerification()
    {
        using var s = Satellite();
        var source = new WorkspaceLexSource([s.LexPath]);

        var a = await source.GetEntryAsync(s.LexPath, "Rates.A");
        Assert.Equal("Rates.A", a!.Key);
        Assert.Equal("7.5", a.Entry.Content);
        Assert.Equal("p. 1", a.Entry.SourceLocation);
        Assert.Equal("rate 7.5", a.Entry.SourceQuote);
        Assert.Equal("Test Act s. 1", a.Entry.LegalReference);
        Assert.Equal(Verification.ModeAuto, a.Entry.Verification.Mode);
        Assert.Equal(["https://example.org/doc"], a.Sources.Select(x => x.Url));
        Assert.Null(a.Review);
        Assert.Equal(ManifestHash.OfFile(s.LexPath), a.ContentHash);

        var b = await source.GetEntryAsync(s.LexPath, "Rates.B");
        Assert.Equal(Source.ReviewFourEyes, b!.Review);
        Assert.Equal("Jane Doe", b.Entry.Verification.VerifiedBy);
    }

    [Fact]
    public async Task Entry_ListsMatchingExclusions()
    {
        using var s = Satellite();
        var source = new WorkspaceLexSource([s.LexPath]);

        var a = await source.GetEntryAsync(s.LexPath, "Rates.A");
        Assert.Equal([Exclusion.KindSourceLocation], a!.Exclusions.Select(x => x.Kind));

        var c = await source.GetEntryAsync(s.LexPath, "Bundle.C");
        var excluded = Assert.Single(c!.Exclusions);
        Assert.Equal(Exclusion.KindNotApplicable, excluded.Kind);
        Assert.Equal("Bundle.C.Rate", excluded.Pattern);
    }

    [Fact]
    public async Task Entry_SnapshotState()
    {
        using var s = Satellite();
        var source = new WorkspaceLexSource([s.LexPath]);
        Assert.Equal(LexEntrySnapshotState.NoSnapshot, (await source.GetEntryAsync(s.LexPath, "Rates.A"))!.SnapshotState);

        await Snapshot(s);
        Assert.All(await source.GetEntriesAsync(s.LexPath)!, x => Assert.Equal(LexEntrySnapshotState.Current, x.SnapshotState));

        var editor = ManifestEditor.Parse(await File.ReadAllTextAsync(s.LexPath));
        editor.UpdateEntry("Rates.A", new EntryUpdate { SourceQuote = "rate of 7.5" });
        editor.AddValue(new Entry { Key = "Rates.D", Content = "1" });
        await editor.SaveAsync(s.LexPath);

        Assert.Equal(LexEntrySnapshotState.Changed, (await source.GetEntryAsync(s.LexPath, "Rates.A"))!.SnapshotState);
        Assert.Equal(LexEntrySnapshotState.Current, (await source.GetEntryAsync(s.LexPath, "Rates.B"))!.SnapshotState);
        Assert.Equal(LexEntrySnapshotState.NotInSnapshot, (await source.GetEntryAsync(s.LexPath, "Rates.D"))!.SnapshotState);
    }

    [Fact]
    public async Task Entries_AreInDocumentOrder()
    {
        using var s = Satellite();
        var source = new WorkspaceLexSource([s.LexPath]);

        var entries = await source.GetEntriesAsync(s.LexPath);

        Assert.Equal(["Rates.A", "Rates.B", "Bundle.C"], entries!.Select(x => x.Key));
    }

    [Fact]
    public async Task UnknownManifestOrKey_IsNull()
    {
        using var s = Satellite();
        var source = new WorkspaceLexSource([s.LexPath]);

        Assert.Null(await source.GetManifestAsync(Path.Combine(s.Root, "other", "lex.json")));
        Assert.Null(await source.GetManifestAsync(null));
        Assert.Null(await source.GetEntriesAsync(Path.Combine(s.Root, "other", "lex.json")));
        Assert.Null(await source.GetEntryAsync(s.LexPath, "Rates.Z"));
        Assert.Null(await source.GetEntryAsync(Path.Combine(s.Root, "other", "lex.json"), "Rates.A"));
    }

    [Fact]
    public async Task UnreadableManifest_IsListedWithError()
    {
        using var s = Satellite();
        var broken = new TestSatellite();
        try
        {
            await File.WriteAllTextAsync(broken.LexPath, "{ broken");
            var source = new WorkspaceLexSource([s.LexPath, broken.LexPath]);

            var list = await source.ListManifestsAsync();

            Assert.Equal(2, list.Count);
            Assert.Null(list[0].Error);
            Assert.NotNull(list[1].Error);
            Assert.Equal(0, list[1].Entries);
            Assert.Null(await source.GetEntriesAsync(broken.LexPath));
        }
        finally
        {
            broken.Dispose();
        }
    }

    [Fact]
    public async Task FromRoot_FindsManifestsOfMatchingRepositories()
    {
        var root = Path.Combine(Path.GetTempPath(), "lexroot-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var repository in new[] { "Repo.A", "Other.YY" })
            {
                var lex = Path.Combine(root, repository, "2026", "Data.Test.2026", ".lex");
                Directory.CreateDirectory(lex);
                await File.WriteAllTextAsync(Path.Combine(lex, "lex.json"), JsonSerializer.Serialize(Manifest(), WriteOptions));
            }
            var source = WorkspaceLexSource.FromRoot(root, "Repo.*");

            var list = await source.ListManifestsAsync();

            var info = Assert.Single(list);
            Assert.Contains("Repo.A", info.Id, StringComparison.Ordinal);
            Assert.NotNull(await source.GetManifestAsync(info.Id));
            var other = Path.Combine(root, "Other.YY", "2026", "Data.Test.2026", ".lex", "lex.json");
            Assert.Null(await source.GetManifestAsync(other));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Source_ReadsAfterEveryChange()
    {
        using var s = Satellite();
        var source = new WorkspaceLexSource([s.LexPath]);
        Assert.Equal(3, (await source.GetManifestAsync(s.LexPath))!.Entries);

        var editor = ManifestEditor.Parse(await File.ReadAllTextAsync(s.LexPath));
        editor.AddValue(new Entry { Key = "Rates.D", Content = "1" });
        await editor.SaveAsync(s.LexPath);

        Assert.Equal(4, (await source.GetManifestAsync(s.LexPath))!.Entries);
    }
}
