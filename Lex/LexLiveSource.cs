using System;
using System.Collections.Generic;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Live state of a source URL in the snapshot: hash of the normalized text, used to detect source changes
/// </summary>
public sealed class LexLiveSource
{
    /// <summary>
    /// Source URL
    /// </summary>
    public string Url { get; init; }

    /// <summary>
    /// Hash of the normalized text (sha256:...); null when the source could not be read
    /// </summary>
    public string TextHash { get; init; }

    /// <summary>
    /// Reason why the source could not be read (e.g. HTTP 403, PDF)
    /// </summary>
    public string Reason { get; init; }

    /// <summary>
    /// Distinct http(s) URLs of the manifest sources and entries, in manifest order
    /// </summary>
    /// <param name="manifest">Manifest</param>
    public static List<string> Urls(LexManifest manifest) =>
        manifest.Sources.Select(x => x.Url)
            .Concat(manifest.Values.Select(x => x.SourceUrl))
            .Where(IsHttp)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static bool IsHttp(string url) =>
        !string.IsNullOrWhiteSpace(url) &&
        (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Live state of a loaded source document
    /// </summary>
    internal static LexLiveSource From(string url, LexSourceDocument document) =>
        document.IsReadable
            ? new LexLiveSource { Url = url, TextHash = document.TextHash() }
            : new LexLiveSource { Url = url, Reason = document.Reason ?? "not readable" };
}
