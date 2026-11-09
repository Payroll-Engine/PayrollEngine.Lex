using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PayrollEngine.Lex.Tests;

/// <summary>
/// Text table entries in the manifest: archive content files and content maps in validation, snapshot,
/// verification and evidence check
/// </summary>
public class TextTableSnapshotTests
{
    private const string ArchiveName = "tariffs-2026.zip";
    private const string EntryName = "tariff-a.txt";
    private const string MapName = "Tariff.map.json";
    private const string ContentFile = ArchiveName + "!" + EntryName;

    private static Entry TableEntry(string contentFile = ContentFile, string contentMap = MapName) => new()
    {
        Key = "Tariff.A",
        ContentFile = contentFile,
        ContentMap = contentMap,
        SourceUrls = ["https://example.org/doc"],
        SourceLocation = "tariff table A",
        LegalReference = "Test Act s. 3"
    };

    private static Manifest TableManifest(Entry entry = null, bool declareArchive = true)
    {
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"), entry ?? TableEntry());
        if (declareArchive)
        {
            manifest.Files.Add(new EvidenceFile { FileName = ArchiveName });
        }
        return manifest;
    }

    private static TestSatellite Satellite(bool writeMap = true)
    {
        var s = new TestSatellite();
        TariffSample.WriteArchive(Path.Combine(s.LexDirectory, ArchiveName), EntryName, TariffSample.Text());
        if (writeMap)
        {
            File.WriteAllText(Path.Combine(s.LexDirectory, MapName), TariffSample.MapJson);
        }
        return s;
    }

    private static string TextHash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    #region Content source

    [Fact]
    public void ContentSource_SplitsArchiveEntry()
    {
        var source = ContentSource.Resolve("C:\\lex", "sub\\tariffs.zip!folder\\tariff-a.txt");
        Assert.True(source.IsArchiveEntry);
        Assert.Equal("tariffs.zip", source.FileName);
        Assert.Equal("folder/tariff-a.txt", source.EntryName);
        Assert.Equal("tariff-a.txt", source.ContentName);
        Assert.Equal("tariffs.zip!folder/tariff-a.txt", source.LocalReference);
    }

    [Fact]
    public void ContentSource_PlainFile_HasNoEntry()
    {
        var source = ContentSource.Resolve("C:\\lex", "rates.json");
        Assert.False(source.IsArchiveEntry);
        Assert.Equal("rates.json", source.ContentName);
        Assert.Equal("rates.json", source.LocalReference);
    }

    [Fact]
    public void ContentSource_ArchiveEntry_IsReadInPlace()
    {
        using var s = Satellite();
        var source = ContentSource.Resolve(s.LexDirectory, ContentFile);
        Assert.Null(source.Problem());
        Assert.Equal(TariffSample.Text(), source.ReadAllText());
        Assert.Equal(TextHash(TariffSample.Text()), source.Hash());
    }

    [Fact]
    public void ContentSource_Problems_AreReported()
    {
        using var s = Satellite();
        File.WriteAllText(Path.Combine(s.LexDirectory, "plain.zip"), "no archive");
        Assert.Contains("archive missing", ContentSource.Resolve(s.LexDirectory, "missing.zip!a.txt").Problem());
        Assert.Contains("entry missing", ContentSource.Resolve(s.LexDirectory, ArchiveName + "!missing.txt").Problem());
        Assert.Contains("not a ZIP archive", ContentSource.Resolve(s.LexDirectory, "plain.zip!a.txt").Problem());
        Assert.Contains("contentFile missing", ContentSource.Resolve(s.LexDirectory, "missing.json").Problem());
    }

    #endregion

    #region Validator

    [Fact]
    public void Validate_ArchiveEntryWithMap_IsValid()
    {
        using var s = Satellite();
        Assert.Empty(Validator.Validate(TableManifest(), s.LexDirectory));
    }

    [Fact]
    public void Validate_UndeclaredArchive_IsReported()
    {
        using var s = Satellite();
        Assert.Contains(Validator.Validate(TableManifest(declareArchive: false), s.LexDirectory),
            e => e.Contains("must be declared in files[]"));
    }

    [Fact]
    public void Validate_MissingArchiveEntry_IsReported()
    {
        using var s = Satellite();
        Assert.Contains(Validator.Validate(TableManifest(TableEntry(ArchiveName + "!missing.txt")), s.LexDirectory),
            e => e.Contains("entry missing in the archive"));
    }

    [Fact]
    public void Validate_MapWithoutContentFile_IsReported()
    {
        using var s = Satellite();
        var entry = TestSatellite.Value("Tariff.A", "1");
        var manifest = TableManifest(new Entry
        {
            Key = entry.Key, Content = entry.Content, ContentMap = MapName, SourceUrls = entry.SourceUrls,
            SourceLocation = entry.SourceLocation, LegalReference = entry.LegalReference
        });
        Assert.Contains(Validator.Validate(manifest, s.LexDirectory), e => e.Contains("'contentMap' requires 'contentFile'"));
    }

    [Fact]
    public void Validate_MissingMap_IsReported()
    {
        using var s = Satellite(writeMap: false);
        Assert.Contains(Validator.Validate(TableManifest(), s.LexDirectory), e => e.Contains("contentMap not found"));
    }

    [Fact]
    public void Validate_InvalidMap_IsReported()
    {
        using var s = Satellite(writeMap: false);
        File.WriteAllText(Path.Combine(s.LexDirectory, MapName), "{ \"values\": [] }");
        Assert.Contains(Validator.Validate(TableManifest(), s.LexDirectory), e => e.Contains("is invalid"));
    }

    [Fact]
    public void Validate_MapWithBrokenJson_IsReported()
    {
        using var s = Satellite(writeMap: false);
        File.WriteAllText(Path.Combine(s.LexDirectory, MapName), "{ \"key\": ");
        Assert.Contains(Validator.Validate(TableManifest(), s.LexDirectory), e => e.Contains("is not valid JSON"));
    }

    #endregion

    #region Snapshot and verification

    [Fact]
    public async Task Snapshot_ArchiveEntry_IsHashedInPlace()
    {
        using var s = Satellite();
        var manifest = TableManifest();

        var snapshot = await s.WriteSnapshot(manifest);

        var entry = snapshot.Values.Single(x => x.Key == "Tariff.A");
        Assert.Equal(ContentFile, entry.ContentFile);
        Assert.Equal(MapName, entry.ContentMap);
        // the archive is listed once (files[]), the entry is not extracted
        Assert.Single(snapshot.Files, x => x.FileName == ArchiveName);
        Assert.Single(snapshot.Files, x => x.FileName == MapName);
        Assert.False(File.Exists(Path.Combine(s.LexDirectory, EntryName)));

        // source hash order: values (content, map), then files
        var archiveHash = ContentHash.FileHash(Path.Combine(s.LexDirectory, ArchiveName));
        var mapHash = ContentHash.FileHash(Path.Combine(s.LexDirectory, MapName));
        var expected = "7.5" + TextHash(TariffSample.Text()) + mapHash + archiveHash;
        Assert.Equal("sha256:" + ContentHash.StringHash(expected), snapshot.SourceHash);

        Assert.Empty(Verifier.Verify(manifest, snapshot, s.LexDirectory));
    }

    [Fact]
    public async Task Snapshot_MapOutsideLex_IsCopied()
    {
        using var s = Satellite(writeMap: false);
        var mapPath = Path.Combine(s.Root, MapName);
        await File.WriteAllTextAsync(mapPath, TariffSample.MapJson);
        var manifest = TableManifest(TableEntry(contentMap: "../" + MapName));

        var snapshot = await s.WriteSnapshot(manifest);

        Assert.True(File.Exists(Path.Combine(s.LexDirectory, MapName)));
        Assert.Equal(MapName, snapshot.Values.Single(x => x.Key == "Tariff.A").ContentMap);
        Assert.Empty(Verifier.Verify(manifest, snapshot, s.LexDirectory));

        // a changed original is reported against its copy
        await File.WriteAllTextAsync(mapPath, TariffSample.UnfilteredMapJson);
        Assert.Contains(Verifier.Verify(manifest, snapshot, s.LexDirectory), e => e.Contains("original file differs"));
    }

    [Fact]
    public async Task Snapshot_SharedMap_IsListedOnce()
    {
        using var s = Satellite();
        var second = TableEntry();
        var manifest = TableManifest();
        manifest.Values.Add(new Entry
        {
            Key = "Tariff.B", ContentFile = second.ContentFile, ContentMap = second.ContentMap, SourceUrls = second.SourceUrls,
            SourceLocation = second.SourceLocation, LegalReference = second.LegalReference
        });

        var snapshot = await s.WriteSnapshot(manifest);

        Assert.Single(snapshot.Files, x => x.FileName == MapName);
        Assert.Empty(Verifier.Verify(manifest, snapshot, s.LexDirectory));
    }

    [Fact]
    public async Task Snapshot_DryRunWithoutArchive_DoesNotFail()
    {
        using var s = new TestSatellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, MapName), TariffSample.MapJson);
        var manifest = TableManifest(declareArchive: false);
        manifest.Files.Add(new EvidenceFile { FileName = ArchiveName, SourceUrls = ["https://example.org/tariffs.zip"] });

        var snapshot = await new SnapshotWriter().WriteAsync(manifest, s.LexDirectory, s.LexDirectory, dryRun: true);

        Assert.Equal(ContentFile, snapshot.Values.Single(x => x.Key == "Tariff.A").ContentFile);
        Assert.False(File.Exists(Path.Combine(s.LexDirectory, "snapshot.json")));
    }

    [Fact]
    public async Task Verify_ChangedArchiveEntry_IsReported()
    {
        using var s = Satellite();
        var manifest = TableManifest();
        var snapshot = await s.WriteSnapshot(manifest);

        var archivePath = Path.Combine(s.LexDirectory, ArchiveName);
        File.Delete(archivePath);
        TariffSample.WriteArchive(archivePath, EntryName, TariffSample.Text(TariffSample.Tariffs.Take(2)));

        var errors = Verifier.Verify(manifest, snapshot, s.LexDirectory);
        Assert.Contains(errors, e => e.Contains($"File '{ArchiveName}': content differs"));
        Assert.Contains(errors, e => e.StartsWith("sourceHash differs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_ChangedMap_IsReported()
    {
        using var s = Satellite();
        var manifest = TableManifest();
        var snapshot = await s.WriteSnapshot(manifest);

        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, MapName), TariffSample.UnfilteredMapJson);

        var errors = Verifier.Verify(manifest, snapshot, s.LexDirectory);
        Assert.Contains(errors, e => e.Contains($"File '{MapName}': content differs"));
        Assert.Contains(errors, e => e.StartsWith("sourceHash differs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_RemovedMap_IsReported()
    {
        using var s = Satellite();
        var snapshot = await s.WriteSnapshot(TableManifest());

        var errors = Verifier.Verify(TableManifest(TableEntry(contentMap: null)), snapshot, s.LexDirectory);
        Assert.Contains(errors, e => e.Contains("contentMap differs from the snapshot"));
        Assert.Contains(errors, e => e.StartsWith("sourceHash differs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_MissingArchiveEntry_IsReported()
    {
        using var s = Satellite();
        var snapshot = await s.WriteSnapshot(TableManifest());

        var errors = Verifier.Verify(TableManifest(TableEntry(ArchiveName + "!missing.txt")), snapshot, s.LexDirectory);
        Assert.Contains(errors, e => e.Contains("entry missing in the archive"));
    }

    #endregion

    #region Evidence

    [Fact]
    public void UnusedEvidence_ArchiveAndMap_AreUsed()
    {
        using var s = Satellite();
        Assert.Empty(EvidenceCheck.UnusedEvidence(TableManifest(), s.LexDirectory));
    }

    #endregion
}
