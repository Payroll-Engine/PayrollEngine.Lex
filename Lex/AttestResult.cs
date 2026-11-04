namespace PayrollEngine.Lex;

/// <summary>
/// Result of writing a manual verification record or a review
/// </summary>
public enum AttestResult
{
    /// <summary>The record was written</summary>
    Written,

    /// <summary>The same record exists</summary>
    Unchanged,

    /// <summary>A different manual record exists (requires overwrite)</summary>
    ManualExists,

    /// <summary>A different review exists (requires overwrite)</summary>
    ReviewExists,

    /// <summary>The entry has no manual record to review</summary>
    NoManualRecord,

    /// <summary>No entry with the key</summary>
    NotFound
}
