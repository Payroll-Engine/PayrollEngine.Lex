using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// State of a fetched evidence file
/// </summary>
public enum FetchState
{
    /// <summary>The file was created</summary>
    Created,

    /// <summary>The existing file has the same content</summary>
    Unchanged,

    /// <summary>The existing file was replaced</summary>
    Replaced,

    /// <summary>The existing file differs and was kept (requires overwrite)</summary>
    Differs
}

/// <summary>
/// Result of fetching an evidence file
/// </summary>
public sealed class FetchResult
{
    /// <summary>File name in the .lex folder</summary>
    public string FileName { get; init; }

    /// <summary>Full path of the file</summary>
    public string FilePath { get; init; }

    /// <summary>SHA-256 of the downloaded content (lower-case hex)</summary>
    public string Sha256 { get; init; }

    /// <summary>Size of the downloaded content in bytes</summary>
    public long Size { get; init; }

    /// <summary>File state</summary>
    public FetchState State { get; init; }
}

/// <summary>
/// Downloads a source document unchanged into the .lex folder
/// </summary>
public static class Fetcher
{
    /// <summary>
    /// Download a source document into the .lex folder
    /// </summary>
    /// <param name="client">HTTP client</param>
    /// <param name="url">Source URL (http or https)</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <param name="fileName">Target file name, default from the URL</param>
    /// <param name="overwrite">Replace an existing file with different content</param>
    /// <exception cref="HttpRequestException">The source could not be loaded</exception>
    public static async Task<FetchResult> FetchAsync(HttpClient client, string url, string lexDirectory,
        string fileName = null, bool overwrite = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Invalid source URL: {url}", nameof(url));
        }
        if (!string.IsNullOrWhiteSpace(fileName) && !IsPlainFileName(fileName))
        {
            throw new ArgumentException($"Invalid file name (no folder allowed): {fileName}", nameof(fileName));
        }

        using var response = await client.GetAsync(uri);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {url}", null, response.StatusCode);
        }
        var content = await response.Content.ReadAsByteArrayAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        fileName = string.IsNullOrWhiteSpace(fileName) ? FileNameFromUrl(uri, mediaType) : fileName;
        var filePath = Path.Combine(lexDirectory, fileName);
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));

        var state = FetchState.Created;
        if (File.Exists(filePath))
        {
            if (ContentHash.FileHash(filePath) == hash)
            {
                state = FetchState.Unchanged;
            }
            else
            {
                state = overwrite ? FetchState.Replaced : FetchState.Differs;
            }
        }
        if (state is FetchState.Created or FetchState.Replaced)
        {
            await File.WriteAllBytesAsync(filePath, content);
        }

        return new FetchResult
        {
            FileName = fileName,
            FilePath = filePath,
            Sha256 = hash,
            Size = content.LongLength,
            State = state
        };
    }

    /// <summary>
    /// File name from the last URL segment; without extension one is derived from the media type
    /// </summary>
    internal static string FileNameFromUrl(Uri uri, string mediaType)
    {
        var segment = Uri.UnescapeDataString(uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty);
        var name = new string(segment.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray());
        if (string.IsNullOrWhiteSpace(name) || name.Trim('.', '-').Length == 0)
        {
            name = uri.Host;
        }
        if (string.IsNullOrEmpty(Path.GetExtension(name)) || uri.Host == name)
        {
            name += mediaType switch
            {
                "application/pdf" => ".pdf",
                "text/plain" => ".txt",
                "text/csv" => ".csv",
                "application/json" => ".json",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                _ => ".html"
            };
        }
        return name;
    }

    private static bool IsPlainFileName(string fileName) =>
        fileName == Path.GetFileName(fileName) && fileName != "." && fileName != ".." &&
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
