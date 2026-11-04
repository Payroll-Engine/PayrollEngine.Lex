using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Generated snapshot.json — immutable compliance record
/// </summary>
public sealed class LexSnapshot
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
    public List<LexSource> Sources { get; init; }

    /// <summary>
    /// Compliance values — copied from lex.json; contentFile paths resolved to .lex-local names
    /// </summary>
    public List<LexValue> Values { get; init; }

    /// <summary>
    /// All files in the .lex folder — includes both explicit files[] and values with contentFile
    /// </summary>
    public List<LexSnapshotFile> Files { get; init; }
}
