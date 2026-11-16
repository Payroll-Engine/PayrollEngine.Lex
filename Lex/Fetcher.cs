using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Downloads a source document unchanged into the .lex folder
/// </summary>
public static class Fetcher
{
    private const string ManifestFileName = "lex.json";

    /// <summary>
    /// Download a source document into the .lex folder. The manifest (lex.json) and the snapshot
    /// (snapshot.json) are never a target: an explicit file name is rejected before the request,
    /// a file name derived from the URL after the download and before anything is written.
    /// </summary>
    /// <param name="client">HTTP client</param>
    /// <param name="url">Source URL (http or https)</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <param name="fileName">Target file name, default from the URL</param>
    /// <param name="overwrite">Replace an existing file with different content</param>
    /// <param name="targetGuard">Optional check of the full target path, called before the target file is
    /// read or written; an exception on the guard stops the fetch and nothing is written</param>
    /// <exception cref="ArgumentException">Invalid URL, or a file name that is invalid or reserved
    /// (<see cref="IsReservedFileName"/>), parameter <c>fileName</c></exception>
    /// <exception cref="HttpRequestException">The source could not be loaded</exception>
    public static async Task<FetchResult> FetchAsync(HttpClient client, string url, string lexDirectory,
        string fileName = null, bool overwrite = false, Action<string> targetGuard = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Invalid source URL: {url}", nameof(url));
        }
        var explicitName = !string.IsNullOrWhiteSpace(fileName);
        if (explicitName)
        {
            if (!IsPlainFileName(fileName))
            {
                throw new ArgumentException($"Invalid file name (no folder allowed): {fileName}", nameof(fileName));
            }
            EnsureNotReserved(lexDirectory, fileName);
            // Windows drops a trailing dot or space: the name would hit another file than it says
            if (fileName.EndsWith('.') || fileName.EndsWith(' '))
            {
                throw new ArgumentException($"Invalid file name (trailing dot or space): {fileName}", nameof(fileName));
            }
        }

        using var response = await client.GetAsync(uri);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {url}", null, response.StatusCode);
        }
        var content = await response.Content.ReadAsByteArrayAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (!explicitName)
        {
            fileName = FileNameFromUrl(uri, mediaType);
            EnsureNotReserved(lexDirectory, fileName);
        }
        var filePath = Path.Combine(lexDirectory, fileName);
        targetGuard?.Invoke(Path.GetFullPath(filePath));
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

    /// <summary>
    /// The file name is reserved for the manifest (lex.json) or the snapshot (snapshot.json) and is never
    /// a fetch target. The comparison ignores case and trailing dots and spaces (Windows resolves such
    /// names to the same file).
    /// </summary>
    /// <param name="fileName">File name without folder</param>
    /// <returns>True for a reserved name, false for any other name, null or empty</returns>
    public static bool IsReservedFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }
        var name = fileName.TrimEnd('.', ' ');
        return string.Equals(name, ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, SnapshotOperation.SnapshotFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rejects a reserved file name, by name and by the normalized target path
    /// </summary>
    private static void EnsureNotReserved(string lexDirectory, string fileName)
    {
        var targetPath = Path.GetFullPath(Path.Combine(lexDirectory, fileName));
        if (IsReservedFileName(fileName) ||
            string.Equals(targetPath, Path.GetFullPath(Path.Combine(lexDirectory, ManifestFileName)), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetPath, Path.GetFullPath(Path.Combine(lexDirectory, SnapshotOperation.SnapshotFileName)), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Reserved file name (the manifest and the snapshot are no fetch target): {fileName}", nameof(fileName));
        }
    }

    private static bool IsPlainFileName(string fileName) =>
        fileName == Path.GetFileName(fileName) && fileName != "." && fileName != ".." &&
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
