using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Kind of the regulation target of a lex entry
/// </summary>
internal enum EntryTargetKind
{
    /// <summary>No regulation lookup</summary>
    None,

    /// <summary>Existing lookup values (with optional property path)</summary>
    Values,

    /// <summary>Whole lookup, entry backed by a content file</summary>
    Lookup,

    /// <summary>Lookup exists, the value is missing</summary>
    NewValue
}

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

/// <summary>
/// Maps lex entries to regulation lookup values, shared by the check and the apply of a manifest
/// </summary>
internal static class EntryMapping
{
    /// <summary>
    /// Year tag of the manifest (year of validFrom): a content file key may end with it (Lookup.Key.Year)
    /// </summary>
    internal static string EditionTag(Manifest manifest)
    {
        var validFrom = manifest.ValidFrom;
        return validFrom is { Length: >= 4 } && int.TryParse(validFrom[..4], out _) ? validFrom[..4] : null;
    }

    /// <summary>
    /// Resolve the regulation target of a lex entry
    /// </summary>
    internal static EntryTarget Resolve(Entry entry, RegulationIndex index, string editionTag)
    {
        foreach (var (baseKey, remainder) in Splits(entry.Key))
        {
            if (index.TryGetValues(baseKey, out var values))
            {
                return new EntryTarget
                {
                    Kind = EntryTargetKind.Values,
                    BaseKey = baseKey,
                    Remainder = StripEdition(entry, remainder, editionTag),
                    Values = values
                };
            }
        }
        foreach (var (baseKey, remainder) in Splits(entry.Key))
        {
            if (!index.TryGetLookup(baseKey, out var lookup))
            {
                continue;
            }
            var valueKey = StripEdition(entry, remainder, editionTag);
            if (valueKey.Length > 0)
            {
                return new EntryTarget { Kind = EntryTargetKind.NewValue, BaseKey = baseKey, Remainder = valueKey, Lookup = lookup };
            }
            if (entry.HasContentFile)
            {
                return new EntryTarget { Kind = EntryTargetKind.Lookup, BaseKey = baseKey, Lookup = lookup };
            }
            break;
        }
        return new EntryTarget { Kind = EntryTargetKind.None };
    }

    /// <summary>
    /// Content of a lex entry: inline content or the text of a JSON content file
    /// </summary>
    /// <returns>Content or null with the reason</returns>
    internal static string Content(Entry entry, string lexDirectory, out string reason)
    {
        reason = null;
        if (!entry.HasContentFile)
        {
            if (string.IsNullOrWhiteSpace(entry.Content))
            {
                reason = "content is empty";
                return null;
            }
            return entry.Content;
        }
        if (!string.Equals(Path.GetExtension(entry.ContentFile), ".json", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"contentFile is not JSON: {entry.ContentFile}";
            return null;
        }
        var path = ContentHash.ResolvePath(lexDirectory, entry.ContentFile);
        if (!File.Exists(path))
        {
            reason = $"contentFile missing: {entry.ContentFile}";
            return null;
        }
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Content is a JSON object or array
    /// </summary>
    internal static bool IsStructured(string content)
    {
        var text = content?.TrimStart();
        if (string.IsNullOrEmpty(text) || (text[0] != '{' && text[0] != '['))
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(text, Checker.JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Candidate (lookup key, remaining property path) splits of a lex key, longest key first
    /// </summary>
    internal static IEnumerable<(string BaseKey, string Remainder)> Splits(string key)
    {
        yield return (key, string.Empty);
        var index = key.LastIndexOf('.');
        while (index > 0)
        {
            yield return (key[..index], key[(index + 1)..]);
            index = key.LastIndexOf('.', index - 1);
        }
    }

    private static string StripEdition(Entry entry, string remainder, string editionTag)
    {
        if (!entry.HasContentFile || editionTag == null)
        {
            return remainder;
        }
        if (remainder == editionTag)
        {
            return string.Empty;
        }
        var suffix = "." + editionTag;
        return remainder.EndsWith(suffix, StringComparison.Ordinal) ? remainder[..^suffix.Length] : remainder;
    }
}
