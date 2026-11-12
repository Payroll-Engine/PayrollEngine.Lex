using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Summary of a manifest (<see cref="ILexSource"/>)
/// </summary>
public sealed class LexManifestInfo
{
    /// <summary>Identifier of the manifest in its source; the full path of lex.json for a workspace</summary>
    public string Id { get; init; }

    /// <summary>Name of the satellite folder that holds the .lex folder; the workspace source only</summary>
    public string Satellite { get; init; }

    /// <summary>Error text when lex.json cannot be read; all other values are empty then</summary>
    public string Error { get; init; }

    /// <summary>Country or jurisdiction code</summary>
    public string Authority { get; init; }

    /// <summary>Regulation identifier</summary>
    public string Regulation { get; init; }

    /// <summary>Effective date (yyyy-MM-dd)</summary>
    public string ValidFrom { get; init; }

    /// <summary>Issuing authority</summary>
    public string Provider { get; init; }

    /// <summary>Languages of the sources</summary>
    public List<string> Languages { get; init; } = [];

    /// <summary>Number of entries</summary>
    public int Entries { get; init; }

    /// <summary>Entries taken from the previous cycle that need a new verification</summary>
    public int OpenEntries { get; init; }

    /// <summary>Entries with an automatic verification record</summary>
    public int AutoVerified { get; init; }

    /// <summary>Entries with a manual verification record</summary>
    public int ManualVerified { get; init; }

    /// <summary>Entries without verification record</summary>
    public int WithoutVerification { get; init; }

    /// <summary>Number of declared sources</summary>
    public int Sources { get; init; }

    /// <summary>Number of declared evidence files</summary>
    public int Files { get; init; }

    /// <summary>Number of exclusions</summary>
    public int Exclusions { get; init; }

    /// <summary>State of the snapshot</summary>
    public LexSnapshotState SnapshotState { get; init; }

    /// <summary>Differences between manifest and snapshot (empty unless <see cref="SnapshotState"/> is Stale)</summary>
    public List<string> SnapshotDifferences { get; init; } = [];

    /// <summary>Content hash of lex.json (<see cref="ManifestHash"/>)</summary>
    public string ContentHash { get; init; }
}
