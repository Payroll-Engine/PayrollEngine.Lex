namespace PayrollEngine.Lex;

/// <summary>
/// Outcome of the snapshot of one manifest (<see cref="SnapshotOperation"/>)
/// </summary>
public enum SnapshotStatus
{
    /// <summary>Snapshot created, in dry-run mode validated without writing</summary>
    Created,

    /// <summary>snapshot.json exists and overwrite is off: nothing done</summary>
    Skipped,

    /// <summary>lex.json not readable, invalid or the snapshot could not be written</summary>
    Failed
}
