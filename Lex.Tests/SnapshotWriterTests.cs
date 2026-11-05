using Xunit;

namespace PayrollEngine.Lex.Tests;

public class SnapshotWriterTests
{
    private const string DownloadFile = "download.pdf";

    // unreachable address: a download attempt fails the test
    private const string UnreachableUrl = "http://127.0.0.1:9/download.pdf";

    private static Manifest ManifestWithDownload()
    {
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"));
        manifest.Files.Add(new EvidenceFile { FileName = DownloadFile, SourceUrls = [UnreachableUrl] });
        return manifest;
    }

    [Fact]
    public async Task KeepLocalFiles_UsesExistingCopy()
    {
        using var s = new TestSatellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, DownloadFile), "evidence");
        var downloads = new List<string>();

        var snapshot = await new SnapshotWriter().WriteAsync(ManifestWithDownload(), s.LexDirectory, s.LexDirectory,
            dryRun: false, (name, _) => downloads.Add(name), keepLocalFiles: true);

        Assert.Empty(downloads);
        Assert.Equal("evidence", await File.ReadAllTextAsync(Path.Combine(s.LexDirectory, DownloadFile)));
        var file = Assert.Single(snapshot.Files);
        Assert.Equal(ContentHash.FileHash(Path.Combine(s.LexDirectory, DownloadFile)), file.Sha256);
        Assert.Empty(Verifier.Verify(ManifestWithDownload(), snapshot, s.LexDirectory));
    }

    [Fact]
    public async Task KeepLocalFiles_SameHashAsDownloadDryRun()
    {
        using var s = new TestSatellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, DownloadFile), "evidence");

        var kept = await new SnapshotWriter().WriteAsync(ManifestWithDownload(), s.LexDirectory, s.LexDirectory,
            dryRun: true, keepLocalFiles: true);
        var dryRun = await new SnapshotWriter().WriteAsync(ManifestWithDownload(), s.LexDirectory, s.LexDirectory,
            dryRun: true);

        Assert.Equal(dryRun.SourceHash, kept.SourceHash);
    }

    [Fact]
    public async Task KeepLocalFiles_MissingCopy_IsDownloaded()
    {
        using var s = new TestSatellite();
        var downloads = new List<string>();

        await new SnapshotWriter().WriteAsync(ManifestWithDownload(), s.LexDirectory, s.LexDirectory,
            dryRun: true, (name, _) => downloads.Add(name), keepLocalFiles: true);

        Assert.Equal([DownloadFile], downloads);
    }

    [Fact]
    public async Task Default_DownloadsDespiteExistingCopy()
    {
        using var s = new TestSatellite();
        await File.WriteAllTextAsync(Path.Combine(s.LexDirectory, DownloadFile), "evidence");
        var downloads = new List<string>();

        await new SnapshotWriter().WriteAsync(ManifestWithDownload(), s.LexDirectory, s.LexDirectory,
            dryRun: true, (name, _) => downloads.Add(name));

        Assert.Equal([DownloadFile], downloads);
    }
}
