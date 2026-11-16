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
    private const int MaxLinkDepth = 40;

    /// <summary>
    /// Download a source document into the .lex folder. The manifest (lex.json) and the snapshot
    /// (snapshot.json) are never a target: an explicit file name is rejected before the request,
    /// a file name derived from the URL after the download and before anything is written.
    /// A target that is a link out of the .lex folder is neither read nor written.
    /// </summary>
    /// <param name="client">HTTP client</param>
    /// <param name="url">Source URL (http or https)</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <param name="fileName">Target file name, default from the URL</param>
    /// <param name="overwrite">Replace an existing file with different content</param>
    /// <exception cref="ArgumentException">Invalid URL, or an invalid file name, parameter <c>fileName</c></exception>
    /// <exception cref="ReservedFileNameException">The target is the manifest or the snapshot
    /// (<see cref="IsReservedFileName"/> or the same normalized path), parameter <c>fileName</c></exception>
    /// <exception cref="FetchTargetLinkException">The target is a link that resolves outside the .lex folder,
    /// to the manifest or the snapshot, or to no existing file, or cannot be resolved</exception>
    /// <exception cref="HttpRequestException">The source could not be loaded</exception>
    public static Task<FetchResult> FetchAsync(HttpClient client, string url, string lexDirectory,
        string fileName = null, bool overwrite = false) =>
        FetchAsync(client, url, lexDirectory, fileName, overwrite, targetGuard: null);

    /// <summary>
    /// Download a source document into the .lex folder, with a check of the target path. The manifest
    /// (lex.json) and the snapshot (snapshot.json) are never a target: an explicit file name is rejected
    /// before the request, a file name derived from the URL after the download and before anything is written.
    /// A target that is a link out of the .lex folder is neither read nor written.
    /// </summary>
    /// <remarks>
    /// Order of the checks; each failed check stops the fetch and nothing is written:
    /// <list type="number">
    /// <item>source URL</item>
    /// <item>explicit file name, before the request: no folder, not reserved, no trailing dot or space, link check</item>
    /// <item>HTTP request</item>
    /// <item>file name derived from the URL: not reserved</item>
    /// <item>link check of the target</item>
    /// <item><paramref name="targetGuard"/> with the full target path</item>
    /// <item>read and compare an existing target, write</item>
    /// </list>
    /// The link check applies when the target exists as a symbolic link (or junction): its final target,
    /// with all links in the path resolved, must be an existing file inside the real .lex folder (links in
    /// the path of the .lex folder itself are resolved) and must not have a reserved name. Hard links are
    /// not detected.
    /// </remarks>
    /// <param name="client">HTTP client</param>
    /// <param name="url">Source URL (http or https)</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <param name="fileName">Target file name, null or empty for the name from the URL</param>
    /// <param name="overwrite">Replace an existing file with different content</param>
    /// <param name="targetGuard">Check of the full target path, called after the name and link checks and
    /// before the target file is read or written; an exception on the guard stops the fetch and nothing is
    /// written. Null for no check</param>
    /// <exception cref="ArgumentException">Invalid URL, or an invalid file name, parameter <c>fileName</c></exception>
    /// <exception cref="ReservedFileNameException">The target is the manifest or the snapshot
    /// (<see cref="IsReservedFileName"/> or the same normalized path), parameter <c>fileName</c></exception>
    /// <exception cref="FetchTargetLinkException">The target is a link that resolves outside the .lex folder,
    /// to the manifest or the snapshot, or to no existing file, or cannot be resolved</exception>
    /// <exception cref="HttpRequestException">The source could not be loaded</exception>
    public static async Task<FetchResult> FetchAsync(HttpClient client, string url, string lexDirectory,
        string fileName, bool overwrite, Action<string> targetGuard)
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
            EnsureTargetNotLinkedOut(lexDirectory, fileName);
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
        // an explicit name a second time: the target can change during the download
        EnsureTargetNotLinkedOut(lexDirectory, fileName);
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
            throw new ReservedFileNameException(fileName);
        }
    }

    /// <summary>
    /// Rejects a target that is a link resolving outside the real .lex folder, to a reserved file name,
    /// or to no existing file. A target that does not exist or is no link passes
    /// </summary>
    private static void EnsureTargetNotLinkedOut(string lexDirectory, string fileName)
    {
        var targetPath = Path.GetFullPath(Path.Combine(lexDirectory, fileName));
        var linkTarget = new FileInfo(targetPath).LinkTarget;
        if (linkTarget == null)
        {
            return;
        }

        var realDirectory = RealPath(Path.GetFullPath(lexDirectory), 0);
        string resolvedPath;
        try
        {
            resolvedPath = RealPath(targetPath, 0);
        }
        catch (IOException)
        {
            // broken chain or link cycle
            throw new FetchTargetLinkException(fileName, linkTarget, "cannot be resolved");
        }
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var directoryPrefix = Path.EndsInDirectorySeparator(realDirectory)
            ? realDirectory
            : realDirectory + Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(directoryPrefix, comparison))
        {
            throw new FetchTargetLinkException(fileName, resolvedPath, "leaves the .lex folder");
        }
        if (IsReservedFileName(Path.GetFileName(resolvedPath)))
        {
            throw new FetchTargetLinkException(fileName, resolvedPath, "points to the manifest or the snapshot");
        }
        if (!File.Exists(resolvedPath))
        {
            throw new FetchTargetLinkException(fileName, resolvedPath, "points to no existing file");
        }
    }

    /// <summary>
    /// Full path with every link in the path resolved to its final target
    /// </summary>
    private static string RealPath(string fullPath, int depth)
    {
        if (depth > MaxLinkDepth)
        {
            throw new IOException($"Too many levels of links: {fullPath}");
        }
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var current = root;
        var segments = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget == null)
            {
                continue;
            }
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            // the final target is no link, but its parent folders can be
            current = target == null ? current : RealPath(Path.GetFullPath(target.FullName), depth + 1);
        }
        return current;
    }

    private static bool IsPlainFileName(string fileName) =>
        fileName == Path.GetFileName(fileName) && fileName != "." && fileName != ".." &&
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
