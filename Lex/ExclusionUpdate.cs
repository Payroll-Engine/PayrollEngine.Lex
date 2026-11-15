namespace PayrollEngine.Lex;

/// <summary>
/// Properties of an exclusion to change (<see cref="ManifestEditor.UpdateExclusion"/>). Kind and pattern are the
/// identity of an exclusion and cannot be changed; a property set to null stays as it is.
/// </summary>
public sealed class ExclusionUpdate
{
    /// <summary>New justification (never empty)</summary>
    public string Reason { get; init; }
}
