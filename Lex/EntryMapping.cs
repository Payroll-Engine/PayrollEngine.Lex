using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PayrollEngine.Lex;

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
        // a text table fills a whole lookup: the key names the lookup (with optional edition tag)
        if (entry.HasContentMap)
        {
            foreach (var (baseKey, remainder) in Splits(entry.Key))
            {
                if (index.TryGetLookup(baseKey, out var lookup) && StripEdition(entry, remainder, editionTag).Length == 0)
                {
                    return new EntryTarget { Kind = EntryTargetKind.Table, BaseKey = baseKey, Lookup = lookup };
                }
            }
            return new EntryTarget { Kind = EntryTargetKind.None };
        }
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
    /// Content of a lex entry: inline content or the text of a JSON content file (also inside an archive)
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
        // a text table is converted by its map, its text is no lookup value
        if (entry.HasContentMap)
        {
            reason = $"contentFile is a text table: {entry.ContentFile}";
            return null;
        }
        var source = ContentSource.Resolve(lexDirectory, entry.ContentFile);
        if (!string.Equals(Path.GetExtension(source.ContentName), ".json", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"contentFile is not JSON: {entry.ContentFile}";
            return null;
        }
        reason = source.Problem();
        return reason == null ? source.ReadAllText() : null;
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
            using var document = JsonDocument.Parse(text, LexJson.DocumentOptions);
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
