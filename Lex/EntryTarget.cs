using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Regulation target of a lex entry
/// </summary>
internal sealed class EntryTarget
{
    /// <summary>What the entry key addresses</summary>
    internal EntryTargetKind Kind { get; init; }
    /// <summary>Matched part of the entry key (Lookup.Key or Lookup)</summary>
    internal string BaseKey { get; init; }
    /// <summary>Property path after the base key, empty for the whole value</summary>
    internal string Remainder { get; init; } = string.Empty;
    /// <summary>Addressed regulation values (kind Values)</summary>
    internal List<RegulationValue> Values { get; init; } = [];
    /// <summary>Addressed regulation lookup (kind Lookup)</summary>
    internal RegulationLookup Lookup { get; init; }
}
