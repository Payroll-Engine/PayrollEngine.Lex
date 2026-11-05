using System;
using System.Collections.Generic;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Live state of a source URL in the snapshot: hash of the normalized text, used to detect source changes
/// </summary>
public sealed class LiveSource
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
    /// Reason why the source could not be read (e.g. HTTP 403, no text layer)
    /// </summary>
    public string Reason { get; init; }

    /// <summary>
    /// Distinct http(s) URLs of the manifest sources and entries, in manifest order
    /// </summary>
    /// <param name="manifest">Manifest</param>
    public static List<string> Urls(Manifest manifest) =>
        manifest.Sources.Select(x => x.Url)
            .Concat(manifest.Values.SelectMany(x => x.Urls))
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
    internal static LiveSource From(string url, SourceDocument document) =>
        document.IsReadable
            ? new LiveSource { Url = url, TextHash = document.TextHash() }
            : new LiveSource { Url = url, Reason = document.Reason ?? "not readable" };
}
