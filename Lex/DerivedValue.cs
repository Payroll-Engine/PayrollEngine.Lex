using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Property of a lex entry that is derived from other values (exclusion kind <c>derived</c>):
/// its numbers are not looked up in the source
/// </summary>
public sealed class DerivedValue
{
    /// <summary>Property path within the entry content (e.g. <c>CreditReduction</c> or <c>Group.Rate</c>)</summary>
    public string Property { get; internal init; }

    /// <summary>Value of the property in the entry content</summary>
    public string Value { get; internal init; }

    /// <summary>Formula and legal reference (reason of the exclusion)</summary>
    public string Reason { get; internal init; }
}

/// <summary>
/// Resolves the derived exclusions of a manifest to entry properties
/// </summary>
internal static class DerivedProperties
{
    private const int MaxValueLength = 60;

    /// <summary>
    /// Entry key and property path addressed by a derived pattern: the longest entry key the pattern starts with
    /// (<see cref="EntryMapping.Splits"/>). The path is empty when the pattern is an entry key; the key is null
    /// when the pattern addresses no entry.
    /// </summary>
    /// <param name="pattern">Derived pattern (Lookup.Key.Property)</param>
    /// <param name="keys">Entry keys of the manifest</param>
    internal static (string Key, string Path) Resolve(string pattern, ICollection<string> keys)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return (null, null);
        }
        foreach (var (baseKey, remainder) in EntryMapping.Splits(pattern.Trim()))
        {
            if (keys.Contains(baseKey))
            {
                return (baseKey, remainder);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Entry keys of a manifest
    /// </summary>
    internal static HashSet<string> Keys(Manifest manifest) =>
        manifest.Values.Select(x => x.Key).Where(x => !string.IsNullOrEmpty(x)).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Derived exclusions of a manifest that address a property of an entry
    /// </summary>
    internal static List<Exclusion> Of(string entryKey, IEnumerable<Exclusion> exclusions, ICollection<string> keys) =>
        exclusions
            .Where(x => x.Kind == Exclusion.KindDerived && Resolve(x.Pattern, keys) is var (key, path) &&
                        string.Equals(key, entryKey, StringComparison.Ordinal) && !string.IsNullOrEmpty(path))
            .ToList();

    /// <summary>
    /// Derived values of an entry: the properties of the content addressed by the derived exclusions.
    /// A pattern whose property is missing in the content is ignored (reported by the checker).
    /// </summary>
    /// <param name="entryKey">Entry key</param>
    /// <param name="content">Entry content (inline content or text of the content file)</param>
    /// <param name="exclusions">Exclusions; only the derived exclusions of the entry are used</param>
    internal static List<DerivedValue> Values(string entryKey, string content, IEnumerable<Exclusion> exclusions)
    {
        var values = new List<DerivedValue>();
        var derived = exclusions == null || string.IsNullOrEmpty(entryKey) || string.IsNullOrWhiteSpace(content)
            ? []
            : Of(entryKey, exclusions, [entryKey]);
        if (derived.Count == 0)
        {
            return values;
        }
        try
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            using var document = JsonDocument.Parse(content, Checker.JsonOptions);
            foreach (var exclusion in derived)
            {
                var path = Resolve(exclusion.Pattern, [entryKey]).Path;
                if (values.Any(x => string.Equals(x.Property, path, StringComparison.OrdinalIgnoreCase)) ||
                    !TryGet(document.RootElement, path, out var element))
                {
                    continue;
                }
                values.Add(new DerivedValue { Property = path, Value = Text(element), Reason = exclusion.Reason });
            }
        }
        catch (JsonException)
        {
            // content is no JSON object: no property to derive
        }
        return values;
    }

    /// <summary>
    /// Test if a property path exists in a JSON content
    /// </summary>
    internal static bool Exists(string content, string path)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrEmpty(path))
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(content, Checker.JsonOptions);
            return TryGet(document.RootElement, path, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Property of a JSON object by path (object properties separated by a dot, names ignoring case)
    /// </summary>
    private static bool TryGet(JsonElement root, string path, out JsonElement element)
    {
        element = root;
        foreach (var part in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            var found = false;
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, part, StringComparison.OrdinalIgnoreCase))
                {
                    element = property.Value;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                return false;
            }
        }
        return true;
    }

    private static string Text(JsonElement element)
    {
        var text = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
        return text is { Length: > MaxValueLength } ? text[..MaxValueLength] + "..." : text;
    }
}
