namespace PayrollEngine.Lex;

/// <summary>
/// Regulation value written from a lex entry
/// </summary>
public sealed class ApplyChange
{
    /// <summary>Lex entry key</summary>
    public string EntryKey { get; init; }

    /// <summary>Regulation file name</summary>
    public string File { get; init; }

    /// <summary>Regulation target: Lookup.Key with optional /property path</summary>
    public string Target { get; init; }

    /// <summary>Kind of change</summary>
    public ApplyChangeKind Kind { get; init; }

    /// <summary>Difference before the change</summary>
    public string Detail { get; init; }
}
