using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// An entry with the context a reader needs (<see cref="ILexSource"/>)
/// </summary>
public sealed class LexEntryInfo
{
    /// <summary>Identifier of the manifest</summary>
    public string ManifestId { get; init; }

    /// <summary>Entry key</summary>
    public string Key => Entry?.Key;

    /// <summary>The entry with content, source URLs, location, quote, legal reference and verification record</summary>
    public Entry Entry { get; init; }

    /// <summary>The declared sources whose URL is listed in the source URLs of the entry</summary>
    public List<Source> Sources { get; init; } = [];

    /// <summary>Review rule of the sources: FourEyes when a source requires it, null without rule</summary>
    public string Review { get; init; }

    /// <summary>The exclusions of the manifest that address the entry, including property exclusions</summary>
    public List<Exclusion> Exclusions { get; init; } = [];

    /// <summary>State against the snapshot</summary>
    public LexEntrySnapshotState SnapshotState { get; init; }

    /// <summary>Content hash of lex.json the entry was read from (<see cref="ManifestHash"/>)</summary>
    public string ContentHash { get; init; }
}
