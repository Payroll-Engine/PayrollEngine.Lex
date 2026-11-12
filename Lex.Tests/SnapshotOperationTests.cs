using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class SnapshotOperationTests
{
    private const string DownloadFile = "download.pdf";

    // unreachable address: a download attempt fails the test
    private const string UnreachableUrl = "http://127.0.0.1:9/download.pdf";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static TestSatellite Satellite(Manifest manifest = null)
    {
        var s = new TestSatellite();
        File.WriteAllText(s.LexPath, JsonSerializer.Serialize(manifest ?? TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "7.5"), TestSatellite.Value("Rates.B", "8.5")), WriteOptions));
        return s;
    }

    private static string SnapshotPath(TestSatellite s) => Path.Combine(s.LexDirectory, "snapshot.json");

    [Fact]
    public async Task Snapshot_IsCreated()
    {
        using var s = Satellite();

        var result = await SnapshotOperation.RunAsync(s.LexPath);

        Assert.Equal(SnapshotStatus.Created, result.Status);
        Assert.False(result.DryRun);
        Assert.Equal(SnapshotPath(s), result.SnapshotPath);
        Assert.Equal(2, result.Snapshot.Values.Count);
        Assert.True(File.Exists(SnapshotPath(s)));
        Assert.Null(result.Snapshot.LiveSources);
        Assert.Equal(0, result.LiveSourcesHashed);
        Assert.Equal(0, result.LiveSourcesUnreadable);
    }

    [Fact]
    public async Task ExistingSnapshot_IsSkipped()
    {
        using var s = Satellite();
        await File.WriteAllTextAsync(SnapshotPath(s), "existing");

        var result = await SnapshotOperation.RunAsync(s.LexPath);

        Assert.Equal(SnapshotStatus.Skipped, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Equal("existing", await File.ReadAllTextAsync(SnapshotPath(s)));
    }

    [Fact]
    public async Task ExistingSnapshot_IsSkippedInDryRun()
    {
        using var s = Satellite();
        await File.WriteAllTextAsync(SnapshotPath(s), "existing");

        var result = await SnapshotOperation.RunAsync(s.LexPath, dryRun: true);

        Assert.Equal(SnapshotStatus.Skipped, result.Status);
    }

    [Fact]
    public async Task ExistingSnapshot_IsOverwritten()
    {
        using var s = Satellite();
        await File.WriteAllTextAsync(SnapshotPath(s), "existing");

        var result = await SnapshotOperation.RunAsync(s.LexPath, overwrite: true);

        Assert.Equal(SnapshotStatus.Created, result.Status);
        Assert.NotEqual("existing", await File.ReadAllTextAsync(SnapshotPath(s)));
    }

    [Fact]
    public async Task DryRun_WritesNothing()
    {
        using var s = Satellite();

        var result = await SnapshotOperation.RunAsync(s.LexPath, dryRun: true);

        Assert.Equal(SnapshotStatus.Created, result.Status);
        Assert.True(result.DryRun);
        Assert.Equal(2, result.Snapshot.Values.Count);
        Assert.False(File.Exists(SnapshotPath(s)));
    }

    [Fact]
    public async Task DryRun_ReportsNoDownload()
    {
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"));
        manifest.Files.Add(new EvidenceFile { FileName = DownloadFile, SourceUrls = [UnreachableUrl] });
        using var s = Satellite(manifest);
        var downloads = new List<string>();

        var result = await SnapshotOperation.RunAsync(s.LexPath, dryRun: true, downloading: downloads.Add);

        Assert.Equal(SnapshotStatus.Created, result.Status);
        Assert.Empty(downloads);
    }

    [Fact]
    public async Task Overwrite_KeepsLocalCopy()
    {
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"));
        manifest.Files.Add(new EvidenceFile { FileName = DownloadFile, SourceUrls = [UnreachableUrl] });
        using var s = Satellite(manifest);
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, DownloadFile), "evidence");
        await File.WriteAllTextAsync(SnapshotPath(s), "existing");
        var downloads = new List<string>();

        var result = await SnapshotOperation.RunAsync(s.LexPath, overwrite: true, downloading: downloads.Add);

        Assert.Equal(SnapshotStatus.Created, result.Status);
        Assert.Empty(downloads);
        Assert.Single(result.Snapshot.Files);
    }

    [Fact]
    public async Task FailedDownload_IsFailed()
    {
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"));
        manifest.Files.Add(new EvidenceFile { FileName = DownloadFile, SourceUrls = [UnreachableUrl] });
        using var s = Satellite(manifest);
        var downloads = new List<string>();

        var result = await SnapshotOperation.RunAsync(s.LexPath, downloading: downloads.Add);

        Assert.Equal(SnapshotStatus.Failed, result.Status);
        Assert.Equal([DownloadFile], downloads);
        Assert.NotNull(result.Error);
        Assert.False(File.Exists(SnapshotPath(s)));
    }

    [Fact]
    public async Task TargetPath_IsCreated()
    {
        using var s = Satellite();
        var target = Path.Combine(s.Root, "out");

        var result = await SnapshotOperation.RunAsync(s.LexPath, target);

        Assert.Equal(SnapshotStatus.Created, result.Status);
        Assert.Equal(Path.Combine(target, "snapshot.json"), result.SnapshotPath);
        Assert.True(File.Exists(result.SnapshotPath));
        Assert.False(File.Exists(SnapshotPath(s)));
    }

    [Fact]
    public async Task UnwritableTarget_IsFailed()
    {
        using var s = Satellite();
        var target = Path.Combine(s.Root, "file.txt");
        await File.WriteAllTextAsync(target, "not a folder");

        var result = await SnapshotOperation.RunAsync(s.LexPath, target);

        Assert.Equal(SnapshotStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Null(result.ParseError);
    }

    [Fact]
    public async Task Comments_AreParseErrors()
    {
        using var s = Satellite();
        await File.WriteAllTextAsync(s.LexPath, "// comment\n" + await File.ReadAllTextAsync(s.LexPath));

        var result = await SnapshotOperation.RunAsync(s.LexPath);

        Assert.Equal(SnapshotStatus.Failed, result.Status);
        Assert.NotNull(result.ParseError);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public async Task InvalidManifest_ListsAllErrors()
    {
        using var s = new TestSatellite();
        await File.WriteAllTextAsync(s.LexPath, "{ \"values\": [] }");

        var result = await SnapshotOperation.RunAsync(s.LexPath);

        Assert.Equal(SnapshotStatus.Failed, result.Status);
        Assert.Contains("Missing required field: authority", result.ValidationErrors);
        Assert.Contains("Missing required field: regulation", result.ValidationErrors);
        Assert.Null(result.ParseError);
        Assert.Null(result.Error);
        Assert.False(File.Exists(SnapshotPath(s)));
    }
}
