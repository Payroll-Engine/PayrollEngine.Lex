namespace PayrollEngine.Lex;

/// <summary>
/// Property of a lex entry addressed by a property exclusion: derived from other values (exclusion kind
/// <c>derived</c>) or not applicable (exclusion kind <c>notApplicable</c>); its numbers are not looked up in the source
/// </summary>
public sealed class DerivedValue
{
    /// <summary>Property path within the entry content (e.g. <c>Levy</c> or <c>Group.Rate</c>)</summary>
    public string Property { get; internal init; }

    /// <summary>Value of the property in the entry content</summary>
    public string Value { get; internal init; }

    /// <summary>Reason of the exclusion: formula and legal reference (derived) or why the value does not apply
    /// (notApplicable)</summary>
    public string Reason { get; internal init; }
}
