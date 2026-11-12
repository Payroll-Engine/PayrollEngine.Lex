namespace PayrollEngine.Lex;

/// <summary>
/// State of one entry against the snapshot of its manifest
/// </summary>
public enum LexEntrySnapshotState
{
    /// <summary>The manifest has no readable snapshot</summary>
    NoSnapshot,

    /// <summary>The entry is not in the snapshot</summary>
    NotInSnapshot,

    /// <summary>The entry differs from the snapshot (content, content map, quote, reference year or verification record)</summary>
    Changed,

    /// <summary>The entry matches the snapshot</summary>
    Current
}
