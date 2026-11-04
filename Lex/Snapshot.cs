using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Generated snapshot.json — immutable compliance record
/// </summary>
public sealed class Snapshot
{
    /// <summary>
    /// ISO 8601 UTC creation timestamp
    /// </summary>
    public string Created { get; init; }

    /// <summary>
    /// Tool identifier and version (e.g. "pecmd LexSnapshot 1.0")
    /// </summary>
    public string Tool { get; init; }

    /// <summary>
    /// Country/jurisdiction code — from lex.json
    /// </summary>
    public string Authority { get; init; }

    /// <summary>
    /// Regulation identifier — from lex.json
    /// </summary>
    public string Regulation { get; init; }

    /// <summary>
    /// Effective date — from lex.json
    /// </summary>
    public string ValidFrom { get; init; }

    /// <summary>
    /// Issuing authority — from lex.json
    /// </summary>
    public string Provider { get; init; }

    /// <summary>
    /// SHA256 hash computed over all value contents and content file hashes
    /// </summary>
    public string SourceHash { get; init; }

    /// <summary>
    /// Source document references — copied from lex.json
    /// </summary>
    public List<Source> Sources { get; init; }

    /// <summary>
    /// Compliance values — copied from lex.json; contentFile paths resolved to .lex-local names
    /// </summary>
    public List<Entry> Values { get; init; }

    /// <summary>
    /// All files in the .lex folder — includes both explicit files[] and values with contentFile
    /// </summary>
    public List<SnapshotFile> Files { get; init; }

    /// <summary>
    /// Live state of the source URLs (sources and entries) at snapshot time; not part of the source hash.
    /// Compared by LexCheck with source URL checks to detect changed sources.
    /// </summary>
    public List<LiveSource> LiveSources { get; init; }
}
