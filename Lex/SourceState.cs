namespace PayrollEngine.Lex;

/// <summary>
/// Readability of a source document
/// </summary>
internal enum SourceState
{
    /// <summary>Text could be extracted</summary>
    Readable,

    /// <summary>The source could not be requested or holds no text (blocked host, script-only page,
    /// PDF without text layer, encrypted PDF)</summary>
    Unreadable,

    /// <summary>The format has no reader (e.g. a ZIP archive or an unknown binary format)</summary>
    Unsupported,

    /// <summary>No local evidence file exists</summary>
    Missing
}
