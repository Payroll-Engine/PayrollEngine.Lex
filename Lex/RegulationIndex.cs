using System;
using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup values by lex key: Lookup.Key, Alias.Key and the bare value key
/// </summary>
internal sealed class RegulationIndex
{
    private readonly Dictionary<string, List<RegulationValue>> byFullKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RegulationValue>> byShortKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegulationLookup> lookups = new(StringComparer.Ordinal);

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

    internal bool TryGetValues(string key, out List<RegulationValue> values) =>
        byFullKey.TryGetValue(key, out values) || byShortKey.TryGetValue(key, out values);

    internal bool TryGetLookup(string name, out RegulationLookup lookup) =>
        lookups.TryGetValue(name, out lookup);

    private static void Add(Dictionary<string, List<RegulationValue>> map, string key, RegulationValue value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }
        list.Add(value);
    }
}
