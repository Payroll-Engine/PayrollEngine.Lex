using System;
using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup values by lex key: Lookup.Key, Alias.Key and the bare value key
/// </summary>
internal sealed class RegulationIndex
{
    /// <summary>Values by Lookup.Key and Alias.Key; a key can address values of several regulation files</summary>
    private readonly Dictionary<string, List<RegulationValue>> byFullKey = new(StringComparer.Ordinal);
    /// <summary>Values by bare value key, used when the lex key has no lookup prefix</summary>
    private readonly Dictionary<string, List<RegulationValue>> byShortKey = new(StringComparer.Ordinal);
    /// <summary>Lookups by name and alias; the first lookup of a name wins</summary>
    private readonly Dictionary<string, RegulationLookup> lookups = new(StringComparer.Ordinal);

    /// <summary>
    /// Indexes the values and lookups of the regulation
    /// </summary>
    internal RegulationIndex(RegulationData data)
    {
        foreach (var value in data.Values)
        {
            Add(byFullKey, value.Lookup + "." + value.Key, value);
            if (value.LookupAlias != null)
            {
                Add(byFullKey, value.LookupAlias + "." + value.Key, value);
            }
            Add(byShortKey, value.Key, value);
        }
        foreach (var lookup in data.Lookups)
        {
            lookups.TryAdd(lookup.Name, lookup);
            if (lookup.Alias != null)
            {
                lookups.TryAdd(lookup.Alias, lookup);
            }
        }
    }

    /// <summary>
    /// Values of a lex key: the full key first, then the bare value key
    /// </summary>
    internal bool TryGetValues(string key, out List<RegulationValue> values) =>
        byFullKey.TryGetValue(key, out values) || byShortKey.TryGetValue(key, out values);

    /// <summary>
    /// Lookup by name or alias
    /// </summary>
    internal bool TryGetLookup(string name, out RegulationLookup lookup) =>
        lookups.TryGetValue(name, out lookup);

    /// <summary>
    /// Adds a value to the list of its key
    /// </summary>
    private static void Add(Dictionary<string, List<RegulationValue>> map, string key, RegulationValue value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }
        list.Add(value);
    }
}
