namespace PayrollEngine.Lex;

/// <summary>
/// Regulation lookup value or lookup that an entry of the manifest maps to (same mapping as the check and the apply)
/// </summary>
public sealed class RegulationLink
{
    /// <summary>The entry backs an existing regulation value or lookup</summary>
    public bool Linked { get; init; }

    /// <summary>Lookup name and value key the entry maps to (<c>Lookup.Key</c>, or <c>Lookup</c> for a whole lookup)</summary>
    public string Target { get; init; }

    /// <summary>Number of regulation values the entry backs (all values of the lookup for a whole lookup)</summary>
    public int Values { get; init; }
}
