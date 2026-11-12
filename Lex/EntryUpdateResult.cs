namespace PayrollEngine.Lex;

/// <summary>
/// Outcome of <see cref="ManifestEditor.UpdateEntry"/>
/// </summary>
public enum EntryUpdateResult
{
    /// <summary>At least one property was changed</summary>
    Updated,

    /// <summary>All given values already match the entry</summary>
    Unchanged,

    /// <summary>No entry with the key</summary>
    NotFound
}
