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

    /// <summary>Regulation target: Lookup.Key with optional /property path, the lookup name of a table</summary>
    public string Target { get; init; }

    /// <summary>Kind of change</summary>
    public ApplyChangeKind Kind { get; init; }

    /// <summary>Difference before the change</summary>
    public string Detail { get; init; }

    /// <summary>Table rows without lookup value (kind Table)</summary>
    public int AddedValues { get; init; }

    /// <summary>Lookup values without table row (kind Table)</summary>
    public int RemovedValues { get; init; }

    /// <summary>Table rows with a different lookup value (kind Table)</summary>
    public int ChangedValues { get; init; }
}
