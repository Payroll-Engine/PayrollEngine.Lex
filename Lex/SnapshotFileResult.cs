using System.Collections.Generic;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of the snapshot of one manifest (<see cref="SnapshotOperation"/>)
/// </summary>
public sealed class SnapshotFileResult
{
    /// <summary>Path of lex.json</summary>
    public string LexPath { get; internal init; }

    /// <summary>Path of snapshot.json</summary>
    public string SnapshotPath { get; internal init; }

    /// <summary>Dry run: nothing written</summary>
    public bool DryRun { get; internal init; }

    /// <summary>Outcome</summary>
    public SnapshotStatus Status { get; internal init; }

    /// <summary>lex.json could not be parsed (status failed)</summary>
    public string ParseError { get; internal init; }

    /// <summary>Validation errors of the manifest (status failed)</summary>
    public List<string> ValidationErrors { get; internal init; } = [];

    /// <summary>The snapshot could not be written (status failed)</summary>
    public string Error { get; internal init; }

    /// <summary>Created snapshot, null unless the status is created</summary>
    public Snapshot Snapshot { get; internal init; }

    /// <summary>Live sources with a text hash</summary>
    public int LiveSourcesHashed => Snapshot?.LiveSources?.Count(x => x.TextHash != null) ?? 0;

    /// <summary>Live sources that could not be read (no change detection)</summary>
    public int LiveSourcesUnreadable => Snapshot?.LiveSources?.Count(x => x.TextHash == null) ?? 0;
}
