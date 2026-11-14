namespace PayrollEngine.Lex;

/// <summary>
/// Text properties of a declared source to change (<see cref="ManifestEditor.UpdateSource"/>); a property set to
/// null stays as it is
/// </summary>
public sealed class SourceUpdate
{
    /// <summary>New description; an empty text removes the description</summary>
    public string Description { get; init; }

    /// <summary>New legal basis; an empty text removes the legal basis</summary>
    public string LegalBasis { get; init; }
}
