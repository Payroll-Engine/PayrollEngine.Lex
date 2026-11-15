namespace PayrollEngine.Lex;

/// <summary>
/// Outcome of <see cref="ManifestEditor.UpdateExclusion"/>
/// </summary>
public enum ExclusionUpdateResult
{
    /// <summary>The reason was changed</summary>
    Updated,

    /// <summary>All given values already match the exclusion</summary>
    Unchanged,

    /// <summary>No exclusion with the kind and pattern exists</summary>
    NotFound
}
