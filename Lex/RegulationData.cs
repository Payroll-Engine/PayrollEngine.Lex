using System;
using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup values of the Regulation folder of a satellite
/// </summary>
internal sealed class RegulationData
{
    internal string Name { get; set; }
    internal string ValidFrom { get; set; }
    internal int LookupCount { get; set; }
    internal List<RegulationValue> Values { get; } = [];
    internal List<RegulationLookup> Lookups { get; } = [];

    /// <summary>Lookup names including the names without satellite namespace</summary>
    internal HashSet<string> LookupNames { get; } = new(StringComparer.Ordinal);

    /// <summary>validFrom tokens of all regulations</summary>
    internal List<RegulationToken> ValidFromTokens { get; } = [];
}
