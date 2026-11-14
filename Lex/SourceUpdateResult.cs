namespace PayrollEngine.Lex;

/// <summary>
/// Outcome of <see cref="ManifestEditor.UpdateSource"/>
/// </summary>
public enum SourceUpdateResult
{
    /// <summary>At least one property was changed</summary>
    Updated,

    /// <summary>All given values already match the source</summary>
    Unchanged,

    /// <summary>No source with the URL is declared</summary>
    NotFound
}
