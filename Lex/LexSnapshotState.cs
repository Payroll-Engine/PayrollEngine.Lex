namespace PayrollEngine.Lex;

/// <summary>
/// State of the snapshot of a manifest against the manifest and the files in the .lex folder
/// </summary>
public enum LexSnapshotState
{
    /// <summary>No snapshot.json</summary>
    Missing,

    /// <summary>The snapshot matches the manifest and the files (<see cref="Verifier"/> reports no difference)</summary>
    Current,

    /// <summary>The snapshot differs from the manifest or the files</summary>
    Stale,

    /// <summary>snapshot.json cannot be read</summary>
    Unreadable
}
