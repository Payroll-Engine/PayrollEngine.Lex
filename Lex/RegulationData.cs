using System;
using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup values of the Regulation folder of a satellite
/// </summary>
internal sealed class RegulationData
{
    /// <summary>Name of the regulation</summary>
    internal string Name { get; set; }
    /// <summary>validFrom of the regulation</summary>
    internal string ValidFrom { get; set; }
    /// <summary>Number of lookups</summary>
    internal int LookupCount { get; set; }
    /// <summary>Values of all lookups</summary>
    internal List<RegulationValue> Values { get; } = [];
    /// <summary>All lookups</summary>
    internal List<RegulationLookup> Lookups { get; } = [];

    /// <summary>Lookup names including the names without satellite namespace</summary>
    internal HashSet<string> LookupNames { get; } = new(StringComparer.Ordinal);

    /// <summary>validFrom tokens of all regulations</summary>
    internal List<RegulationToken> ValidFromTokens { get; } = [];
}
