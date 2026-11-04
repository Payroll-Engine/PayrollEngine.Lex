using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of requesting one source URL
/// </summary>
internal enum LexUrlStatus
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
public sealed class LexUrlChecker : IDisposable
{
    private readonly HttpClient client;
    private readonly SemaphoreSlim gate = new(6);
    private readonly ConcurrentDictionary<string, Task<LexUrlStatus>> cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Create a URL checker
    /// </summary>
    public LexUrlChecker() :
        this(null)
    {
    }

    /// <param name="handler">Optional message handler (tests)</param>
    internal LexUrlChecker(HttpMessageHandler handler)
    {
        client = handler != null ? new HttpClient(handler) : new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(25);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; PayrollEngine-LexCheck)");
    }

    /// <summary>
    /// Check the URL (cached per run)
    /// </summary>
    internal Task<LexUrlStatus> Check(string url) =>
        cache.GetOrAdd(url, RequestAsync);

    /// <summary>
    /// All distinct http(s) URLs of the manifest: sources, entries and files
    /// </summary>
    internal static IEnumerable<string> Urls(LexManifest manifest) =>
        manifest.Sources.Select(x => x.Url)
            .Concat(manifest.Values.Select(x => x.SourceUrl))
            .Concat(manifest.Files.Select(x => x.SourceUrl))
            .Where(x => !string.IsNullOrWhiteSpace(x) &&
                        (x.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         x.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Request all URLs of the manifest and record the result
    /// </summary>
    public async Task Apply(LexManifest manifest, LexCheckResult result)
    {
        var urls = Urls(manifest).ToList();
        var states = await Task.WhenAll(urls.Select(async url => (url, status: await Check(url))));
        foreach (var (url, status) in states)
        {
            if (status == LexUrlStatus.Dead)
            {
                result.DeadUrls.Add(url);
            }
            else if (status == LexUrlStatus.Blocked)
            {
                result.BlockedUrls++;
            }
        }
        result.UrlsChecked = true;
    }

    private async Task<LexUrlStatus> RequestAsync(string url)
    {
        await gate.WaitAsync();
        try
        {
            var status = await SendAsync(HttpMethod.Head, url);
            // some hosts reject HEAD: the GET answer is authoritative
            return status == LexUrlStatus.Ok ? status : await SendAsync(HttpMethod.Get, url);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LexUrlStatus> SendAsync(HttpMethod method, string url)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var code = (int)response.StatusCode;
            if (code is >= 200 and < 400)
            {
                return LexUrlStatus.Ok;
            }
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound or HttpStatusCode.Gone => LexUrlStatus.Dead,
                _ => LexUrlStatus.Blocked
            };
        }
        catch (HttpRequestException exception) when (exception.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound })
        {
            return LexUrlStatus.Dead;
        }
        catch (Exception)
        {
            // timeout, TLS or network problem: not verifiable
            return LexUrlStatus.Blocked;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        client.Dispose();
        gate.Dispose();
    }
}
