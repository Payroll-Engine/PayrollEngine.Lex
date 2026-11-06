using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Property of a lex entry addressed by a property exclusion: derived from other values (exclusion kind
/// <c>derived</c>) or not applicable (exclusion kind <c>notApplicable</c>); its numbers are not looked up in the source
/// </summary>
public sealed class DerivedValue
{
    /// <summary>Property path within the entry content (e.g. <c>CreditReduction</c> or <c>Group.Rate</c>)</summary>
    public string Property { get; internal init; }

    /// <summary>Value of the property in the entry content</summary>
    public string Value { get; internal init; }

    /// <summary>Reason of the exclusion: formula and legal reference (derived) or why the value does not apply
    /// (notApplicable)</summary>
    public string Reason { get; internal init; }
}

/// <summary>
/// Resolves the property exclusions (derived, notApplicable) of a manifest to entry properties
/// </summary>
internal static class DerivedProperties
{
    private const int MaxValueLength = 60;

    /// <summary>
    /// Test if an exclusion kind addresses a property of an entry (Lookup.Key.Property)
    /// </summary>
    internal static bool IsPropertyKind(string kind) =>
        kind is Exclusion.KindDerived or Exclusion.KindNotApplicable;

    /// <summary>
    /// Entry key and property path addressed by a property pattern: the longest entry key the pattern starts with
    /// (<see cref="EntryMapping.Splits"/>). The path is empty when the pattern is an entry key; the key is null
    /// when the pattern addresses no entry.
    /// </summary>
    /// <param name="pattern">Property pattern (Lookup.Key.Property)</param>
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
    /// Property exclusions (derived, notApplicable) of a manifest that address a property of an entry
    /// </summary>
    internal static List<Exclusion> Of(string entryKey, IEnumerable<Exclusion> exclusions, ICollection<string> keys) =>
        exclusions
            .Where(x => IsPropertyKind(x.Kind) && Resolve(x.Pattern, keys) is var (key, path) &&
                        string.Equals(key, entryKey, StringComparison.Ordinal) && !string.IsNullOrEmpty(path))
            .ToList();

    /// <summary>
    /// Excluded values of an entry: the properties of the content addressed by the exclusions of a kind.
    /// A pattern whose property is missing in the content is ignored (reported by the checker).
    /// </summary>
    /// <param name="entryKey">Entry key</param>
    /// <param name="content">Entry content (inline content or text of the content file)</param>
    /// <param name="exclusions">Exclusions; only the exclusions of the kind and the entry are used</param>
    /// <param name="kind">Exclusion kind, default derived</param>
    internal static List<DerivedValue> Values(string entryKey, string content, IEnumerable<Exclusion> exclusions,
        string kind = Exclusion.KindDerived)
    {
        var values = new List<DerivedValue>();
        var excluded = exclusions == null || string.IsNullOrEmpty(entryKey) || string.IsNullOrWhiteSpace(content)
            ? []
            : Of(entryKey, exclusions.Where(x => x.Kind == kind), [entryKey]);
        if (excluded.Count == 0)
        {
            return values;
        }
        try
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            using var document = JsonDocument.Parse(content, Checker.JsonOptions);
            foreach (var exclusion in excluded)
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
            // content is no JSON object: no property to exclude
        }
        return values;
    }

    /// <summary>
    /// Property paths of a bundle (JSON object content) whose value is 0, number or numeric text.
    /// The path runs over objects only: a 0 within an array (e.g. the first tier of a table) is not listed.
    /// </summary>
    /// <param name="content">Entry content (inline content or text of the content file)</param>
    internal static List<string> Zeros(string content)
    {
        var zeros = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return zeros;
        }
        try
        {
            using var document = JsonDocument.Parse(content, Checker.JsonOptions);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                CollectZeros(document.RootElement, string.Empty, zeros);
            }
        }
        catch (JsonException)
        {
            // content is no JSON object: no bundle
        }
        return zeros;
    }

    private static void CollectZeros(JsonElement element, string path, List<string> zeros)
    {
        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = path.Length == 0 ? property.Name : path + "." + property.Name;
            var value = property.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    CollectZeros(value, propertyPath, zeros);
                    break;
                case JsonValueKind.Number:
                    if (value.TryGetDecimal(out var number) && number == 0m) zeros.Add(propertyPath);
                    break;
                case JsonValueKind.String:
                    if (decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var text) &&
                        text == 0m)
                    {
                        zeros.Add(propertyPath);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Zero properties of a bundle without justification: neither not applicable nor derived, also not below an
    /// excluded group property
    /// </summary>
    /// <param name="zeros">Property paths with the value 0 (<see cref="Zeros"/>)</param>
    /// <param name="justified">Property paths of the notApplicable and derived exclusions of the entry</param>
    internal static List<string> Unjustified(IEnumerable<string> zeros, IEnumerable<string> justified)
    {
        var excluded = justified.Where(x => !string.IsNullOrEmpty(x)).ToList();
        return zeros.Where(zero => !excluded.Any(x =>
                string.Equals(zero, x, StringComparison.OrdinalIgnoreCase) ||
                zero.StartsWith(x + ".", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// Test if a property path exists in a JSON content
    /// </summary>
    internal static bool Exists(string content, string path) =>
        TryGetValue(content, path, out _);

    /// <summary>
    /// Value of a property path in a JSON content: the text of a string, otherwise the raw JSON
    /// </summary>
    internal static bool TryGetValue(string content, string path, out string value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrEmpty(path))
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(content, Checker.JsonOptions);
            if (!TryGet(document.RootElement, path, out var element))
            {
                return false;
            }
            value = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
            return true;
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
