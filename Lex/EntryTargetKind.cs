namespace PayrollEngine.Lex;

/// <summary>
/// Kind of the regulation target of a lex entry
/// </summary>
internal enum EntryTargetKind
{
    /// <summary>No regulation lookup</summary>
    None,

    /// <summary>Existing lookup values (with optional property path)</summary>
    Values,

    /// <summary>Whole lookup, entry backed by a content file</summary>
    Lookup,

    /// <summary>Lookup exists, the value is missing</summary>
    NewValue
}
