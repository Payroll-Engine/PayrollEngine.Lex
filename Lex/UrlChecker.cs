using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of requesting one source URL
/// </summary>
internal enum UrlStatus
{
    /// <summary>The page answers (2xx or redirect to an answering page)</summary>
    Ok,

    /// <summary>The host blocks scripted requests (401, 403, 429) or fails temporarily (5xx, timeout): not verifiable</summary>
    Blocked,

    /// <summary>The page does not exist (404, 410) or the host is unknown</summary>
    Dead
}

/// <summary>
/// Requests the source URLs of lex manifests
/// </summary>
public sealed class UrlChecker : IDisposable
{
    private static readonly JsonSerializerOptions SnapshotOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient client;
    private readonly SemaphoreSlim gate = new(6);
    private readonly ConcurrentDictionary<string, Task<UrlStatus>> cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<SourceDocument>> documents = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers;

    /// <summary>
    /// Create a URL checker
    /// </summary>
    public UrlChecker() :
        this(null, null)
    {
    }

    /// <summary>
    /// Create a URL checker with additional source readers
    /// </summary>
    /// <param name="readers">Additional readers by lower-case file extension (see <see cref="SourceDocument.Load"/>),
    /// used to compare live sources</param>
    public UrlChecker(IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers) :
        this(null, readers)
    {
    }

    /// <param name="handler">Optional message handler (tests)</param>
    /// <param name="readers">Optional readers by file extension</param>
    internal UrlChecker(HttpMessageHandler handler,
        IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers = null)
    {
        this.readers = readers;
        client = handler != null ? new HttpClient(handler) : new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(25);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(SourceDocument.UserAgent);
    }

    /// <summary>
    /// Check the URL (cached per run)
    /// </summary>
    internal Task<UrlStatus> Check(string url) =>
        cache.GetOrAdd(url, RequestAsync);

    /// <summary>
    /// All distinct http(s) URLs of the manifest: sources, entries and files
    /// </summary>
    internal static IEnumerable<string> Urls(Manifest manifest) =>
        manifest.Sources.Select(x => x.Url)
            .Concat(manifest.Values.SelectMany(x => x.Urls))
            .Concat(manifest.Files.SelectMany(x => x.Urls))
            .Where(x => !string.IsNullOrWhiteSpace(x) &&
                        (x.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         x.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Request all URLs of the manifest and record the result. Sources with a text hash in the snapshot
    /// next to lex.json are compared with their live text: a changed text is a finding,
    /// a source that cannot be read is not comparable (note).
    /// </summary>
    public async Task Apply(Manifest manifest, CheckResult result)
    {
        var urls = Urls(manifest).ToList();
        var states = await Task.WhenAll(urls.Select(async url => (url, status: await Check(url))));
        var dead = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (url, status) in states)
        {
            if (status == UrlStatus.Dead)
            {
                result.DeadUrls.Add(url);
                dead.Add(url);
            }
            else if (status == UrlStatus.Blocked)
            {
                result.BlockedUrls++;
            }
        }
        result.UrlsChecked = true;
        await CompareLiveSources(manifest, result, dead);
    }

    private async Task CompareLiveSources(Manifest manifest, CheckResult result, HashSet<string> dead)
    {
        var baseline = ReadBaseline(result.LexPath);
        var live = LiveSource.Urls(manifest).Where(url => !dead.Contains(url)).ToList();
        result.SourcesWithoutBaseline += live.Count(url => !baseline.ContainsKey(url));
        var compared = await Task.WhenAll(live.Where(baseline.ContainsKey).Select(async url =>
        {
            var document = await documents.GetOrAdd(url, LoadDocumentAsync);
            var changed = document.IsReadable &&
                          !string.Equals(document.TextHash(), baseline[url], StringComparison.Ordinal);
            return (url, readable: document.IsReadable, changed);
        }));
        foreach (var (url, readable, changed) in compared)
        {
            // blocked or unreadable: no change finding, the host proves nothing
            if (!readable)
            {
                result.SourcesNotComparable++;
                continue;
            }
            result.SourcesCompared++;
            if (changed)
            {
                result.ChangedSources.Add(url);
            }
        }
    }

    /// <summary>
    /// Text hashes of the snapshot next to lex.json, by URL
    /// </summary>
    private static Dictionary<string, string> ReadBaseline(string lexPath)
    {
        var baseline = new Dictionary<string, string>(StringComparer.Ordinal);
        var snapshotPath = string.IsNullOrWhiteSpace(lexPath)
            ? null
            : Path.Combine(Path.GetDirectoryName(lexPath) ?? string.Empty, "snapshot.json");
        if (snapshotPath == null || !File.Exists(snapshotPath))
        {
            return baseline;
        }
        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(snapshotPath), SnapshotOptions);
            foreach (var source in snapshot?.LiveSources ?? [])
            {
                if (!string.IsNullOrWhiteSpace(source.Url) && source.TextHash != null)
                {
                    baseline[source.Url] = source.TextHash;
                }
            }
        }
        catch (JsonException)
        {
            // an unreadable snapshot is reported by LexVerify
        }
        return baseline;
    }

    private async Task<SourceDocument> LoadDocumentAsync(string url)
    {
        await gate.WaitAsync();
        try
        {
            return await SourceDocument.LoadLive(url, client, readers);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<UrlStatus> RequestAsync(string url)
    {
        await gate.WaitAsync();
        try
        {
            var status = await SendAsync(HttpMethod.Head, url);
            // some hosts reject HEAD: the GET answer is authoritative
            return status == UrlStatus.Ok ? status : await SendAsync(HttpMethod.Get, url);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<UrlStatus> SendAsync(HttpMethod method, string url)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var code = (int)response.StatusCode;
            if (code is >= 200 and < 400)
            {
                return UrlStatus.Ok;
            }
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound or HttpStatusCode.Gone => UrlStatus.Dead,
                _ => UrlStatus.Blocked
            };
        }
        catch (HttpRequestException exception) when (exception.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound })
        {
            return UrlStatus.Dead;
        }
        catch (Exception)
        {
            // timeout, TLS or network problem: not verifiable
            return UrlStatus.Blocked;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        client.Dispose();
        gate.Dispose();
    }
}
