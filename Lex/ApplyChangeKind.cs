namespace PayrollEngine.Lex;

/// <summary>
/// Kind of a regulation change
/// </summary>
public enum ApplyChangeKind
{
    /// <summary>Existing value replaced</summary>
    Changed,

    /// <summary>New lookup value</summary>
    Added,

    /// <summary>All values of a lookup replaced by a text table</summary>
    Table
}
