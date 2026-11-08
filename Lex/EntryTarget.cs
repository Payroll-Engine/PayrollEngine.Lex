using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Regulation target of a lex entry
/// </summary>
internal sealed class EntryTarget
{
    internal EntryTargetKind Kind { get; init; }
    internal string BaseKey { get; init; }
    internal string Remainder { get; init; } = string.Empty;
    internal List<RegulationValue> Values { get; init; } = [];
    internal RegulationLookup Lookup { get; init; }
}
