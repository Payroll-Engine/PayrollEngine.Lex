using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class LiveSourceTests
{
    private const string DocUrl = "https://example.org/doc";
    private const string PageUrl = "https://example.org/page";

    private static readonly string Filler = string.Concat(Enumerable.Repeat("filler text ", 60));

    private static string Page(string rate) =>
        $"<html><body><h2>Rates</h2><p>The rate is {rate}%.</p><p>{Filler}</p></body></html>";

    /// <summary>Answers every request with the page of the URL or the status</summary>
    private sealed class PageHandler(Dictionary<string, (HttpStatusCode Status, string Html)> pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, html) = pages.TryGetValue(request.RequestUri!.ToString(), out var page) ? page : (HttpStatusCode.NotFound, null);
            var response = new HttpResponseMessage(status);
            if (html != null)
            {
                response.Content = new StringContent(html, Encoding.UTF8, "text/html");
            }
            return Task.FromResult(response);
        }
    }

    private static Manifest Manifest()
    {
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "8.5"));
        manifest.Values.Add(new Entry
        {
            Key = "Rates.B", Content = "10", SourceUrls = [PageUrl], SourceLocation = "p. 1", LegalReference = "Test Act s. 2"
        });
        return manifest;
    }

    private static async Task WriteSnapshot(TestSatellite s, Manifest manifest, Dictionary<string, string> pages)
    {
        var snapshot = await new SnapshotWriter().WriteAsync(manifest, s.LexDirectory, s.LexDirectory, dryRun: false,
            loadLiveSource: url => Task.FromResult(pages.TryGetValue(url, out var html)
                ? SourceDocument.FromHtml(html)
                : SourceDocument.FromHtml("<html>blocked</html>")));
        Assert.NotNull(snapshot.LiveSources);
    }

    private static async Task<CheckResult> Check(TestSatellite s, Manifest manifest,
        Dictionary<string, (HttpStatusCode, string)> live)
    {
        var result = new CheckResult { LexPath = s.LexPath, HasSnapshot = true };
        using var checker = new UrlChecker(new PageHandler(live));
        await checker.Apply(manifest, result);
        return result;
    }

    [Fact]
    public void Urls_AreDistinctSourcesAndEntryUrls()
    {
        var manifest = Manifest();
        manifest.Sources.Add(new Source { Url = "file://local" });
        Assert.Equal([DocUrl, PageUrl], LiveSource.Urls(manifest));
    }

    [Fact]
    public async Task Snapshot_RecordsTextHashPerUrl()
    {
        using var s = new TestSatellite();
        var manifest = Manifest();
        var loaded = new List<string>();
        var snapshot = await new SnapshotWriter().WriteAsync(manifest, s.LexDirectory, s.LexDirectory, dryRun: false,
            loadLiveSource: url =>
            {
                lock (loaded) loaded.Add(url);
                return Task.FromResult(url == DocUrl ? SourceDocument.FromHtml(Page("8.5")) : SourceDocument.FromHtml("<html>Loading</html>"));
            });

        Assert.Equal([DocUrl, PageUrl], loaded.Order());
        var doc = snapshot.LiveSources.Single(x => x.Url == DocUrl);
        Assert.Equal(SourceDocument.FromHtml(Page("8.5")).TextHash(), doc.TextHash);
        var page = snapshot.LiveSources.Single(x => x.Url == PageUrl);
        Assert.Null(page.TextHash);
        Assert.NotNull(page.Reason);

        var json = await File.ReadAllTextAsync(Path.Combine(s.LexDirectory, "snapshot.json"));
        var read = JsonSerializer.Deserialize<Snapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal(2, read!.LiveSources.Count);
        Assert.Empty(Verifier.Verify(manifest, read, s.LexDirectory));
    }

    [Fact]
    public async Task Snapshot_LiveSources_DoNotChangeSourceHash()
    {
        using var s = new TestSatellite();
        var without = await new SnapshotWriter().WriteAsync(Manifest(), s.LexDirectory, s.LexDirectory, dryRun: true);
        var with = await new SnapshotWriter().WriteAsync(Manifest(), s.LexDirectory, s.LexDirectory, dryRun: false,
            loadLiveSource: _ => Task.FromResult(SourceDocument.FromHtml(Page("8.5"))));
        Assert.Equal(without.SourceHash, with.SourceHash);
        Assert.Null(without.LiveSources);
    }

    [Fact]
    public async Task Snapshot_DryRun_LoadsNothing()
    {
        using var s = new TestSatellite();
        var called = false;
        var snapshot = await new SnapshotWriter().WriteAsync(Manifest(), s.LexDirectory, s.LexDirectory, dryRun: true,
            loadLiveSource: _ =>
            {
                called = true;
                return Task.FromResult(SourceDocument.FromHtml(Page("8.5")));
            });
        Assert.False(called);
        Assert.Null(snapshot.LiveSources);
    }

    [Fact]
    public async Task CheckUrls_UnchangedSource_IsNoFinding()
    {
        using var s = new TestSatellite();
        var manifest = Manifest();
        await WriteSnapshot(s, manifest, new() { [DocUrl] = Page("8.5"), [PageUrl] = Page("10") });

        var result = await Check(s, manifest, new()
        {
            [DocUrl] = (HttpStatusCode.OK, Page("8.5")),
            [PageUrl] = (HttpStatusCode.OK, Page("10"))
        });

        Assert.Empty(result.ChangedSources);
        Assert.Equal(2, result.SourcesCompared);
    }

    [Fact]
    public async Task CheckUrls_ChangedSource_IsFinding()
    {
        using var s = new TestSatellite();
        var manifest = Manifest();
        await WriteSnapshot(s, manifest, new() { [DocUrl] = Page("8.5"), [PageUrl] = Page("10") });

        var result = await Check(s, manifest, new()
        {
            [DocUrl] = (HttpStatusCode.OK, Page("9.0")),
            [PageUrl] = (HttpStatusCode.OK, Page("10"))
        });

        Assert.Equal([DocUrl], result.ChangedSources);
        Assert.True(result.HasFindings);
        Assert.Contains("Source changed, check value: " + DocUrl, CheckReport.BuildMarkdown([result]));
    }

    [Fact]
    public async Task CheckUrls_BlockedSource_IsNotComparable()
    {
        using var s = new TestSatellite();
        var manifest = Manifest();
        await WriteSnapshot(s, manifest, new() { [DocUrl] = Page("8.5"), [PageUrl] = Page("10") });

        var result = await Check(s, manifest, new()
        {
            [DocUrl] = (HttpStatusCode.Forbidden, null),
            [PageUrl] = (HttpStatusCode.OK, Page("10"))
        });

        Assert.Empty(result.ChangedSources);
        Assert.Equal(1, result.SourcesNotComparable);
        Assert.Equal(1, result.BlockedUrls);
        Assert.False(result.HasFindings);
    }

    [Fact]
    public async Task CheckUrls_SnapshotWithoutTextHash_IsNoFinding()
    {
        using var s = new TestSatellite();
        var manifest = Manifest();
        await new SnapshotWriter().WriteAsync(manifest, s.LexDirectory, s.LexDirectory, dryRun: false);

        var result = await Check(s, manifest, new()
        {
            [DocUrl] = (HttpStatusCode.OK, Page("9.0")),
            [PageUrl] = (HttpStatusCode.OK, Page("10"))
        });

        Assert.Empty(result.ChangedSources);
        Assert.Equal(2, result.SourcesWithoutBaseline);
    }
}
