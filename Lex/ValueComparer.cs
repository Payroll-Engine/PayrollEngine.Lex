using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Comparison of regulation lookup values with lex entry contents
/// </summary>
internal static class ValueComparer
{
    /// <summary>
    /// Content to compare: inline content, or the text of a JSON content file (null without comparable content)
    /// </summary>
    internal static string CompareContent(Entry entry, string lexDirectory)
    {
        if (!entry.HasContentFile)
        {
            return entry.Content;
        }
        try
        {
            return EntryMapping.Content(entry, lexDirectory, out _);
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static void Compare(CheckResult result, string entryKey, string content, RegulationValue value, string remainder)
    {
        if (!Resolve(value, remainder, out var target, out var scalar, out var problem))
        {
            result.UnresolvedSplit++;
            CheckResult.AddSample(result.OrphanSamples, $"{entryKey}: {problem}");
            return;
        }
        if (target == null && scalar == null)
        {
            return;
        }
        result.Compared++;
        var difference = Difference(target, scalar, content);
        if (difference != null)
        {
            result.Drift++;
            CheckResult.AddSample(result.DriftSamples, entryKey + ": " + difference);
        }
    }

    /// <summary>
    /// Resolves the property path of a split key (Lookup.Key.Property) in the regulation value
    /// </summary>
    /// <returns>False with a problem text when the path does not exist</returns>
    private static bool Resolve(RegulationValue value, string remainder, out JsonElement? target, out string scalar,
        out string problem)
    {
        target = null;
        scalar = null;
        problem = null;
        if (remainder.Length == 0)
        {
            target = value.Object;
            scalar = value.Scalar;
            return true;
        }
        if (value.Object == null)
        {
            problem = $"path '{remainder}' but the regulation value is a scalar";
            return false;
        }
        var current = value.Object.Value;
        foreach (var part in remainder.Split('.'))
        {
            var next = Property(current, part);
            if (next == null)
            {
                problem = $"property '{part}' not found in the regulation value";
                return false;
            }
            current = next.Value;
        }
        if (current.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            target = current;
        }
        else
        {
            scalar = current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
        }
        return true;
    }

    /// <summary>
    /// Property of a JSON object by name ignoring case, null if missing or not an object
    /// </summary>
    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Difference between a regulation value (structured or scalar) and the lex content, null if equal
    /// </summary>
    internal static string Difference(JsonElement? structured, string scalar, string content)
    {
        if (structured != null)
        {
            try
            {
                using var lexJson = JsonDocument.Parse(content, LexJson.DocumentOptions);
                var differences = new List<string>();
                DeepDifference(structured.Value, lexJson.RootElement, string.Empty, differences);
                return DifferenceSummary(differences);
            }
            catch (JsonException)
            {
                return "lex content is not JSON but the regulation value is structured";
            }
        }
        return ScalarEquals(scalar, content)
            ? null
            : $"regulation '{Shorten(scalar)}' vs lex '{Shorten(content)}'";
    }

    /// <summary>Number of differences named in the summary of a structured value</summary>
    private const int MaxNamedDifferences = 3;

    /// <summary>
    /// Single difference as is, several as count with the first ones, null without difference
    /// </summary>
    private static string DifferenceSummary(List<string> differences)
    {
        if (differences.Count == 0)
        {
            return null;
        }
        if (differences.Count == 1)
        {
            return differences[0];
        }
        var named = string.Join("; ", differences.Take(MaxNamedDifferences));
        var more = differences.Count > MaxNamedDifferences ? $" (+{differences.Count - MaxNamedDifferences} more)" : string.Empty;
        return $"{differences.Count} differences: {named}{more}";
    }

    private static void DeepDifference(JsonElement regulation, JsonElement lex, string path, List<string> differences)
    {
        if (regulation.ValueKind == JsonValueKind.Object && lex.ValueKind == JsonValueKind.Object)
        {
            var lexProperties = lex.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var property in regulation.EnumerateObject())
            {
                if (!lexProperties.TryGetValue(property.Name, out var lexValue))
                {
                    differences.Add($"{path}/{property.Name} missing in lex");
                    continue;
                }
                DeepDifference(property.Value, lexValue, path + "/" + property.Name, differences);
            }
            var names = regulation.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in lexProperties.Keys.Where(n => !names.Contains(n)))
            {
                differences.Add($"{path}/{name} only in lex");
            }
            return;
        }
        if (regulation.ValueKind == JsonValueKind.Array && lex.ValueKind == JsonValueKind.Array)
        {
            var left = regulation.EnumerateArray().ToList();
            var right = lex.EnumerateArray().ToList();
            if (left.Count != right.Count)
            {
                differences.Add($"{path} array length {left.Count} vs {right.Count}");
                return;
            }
            for (var i = 0; i < left.Count; i++)
            {
                DeepDifference(left[i], right[i], $"{path}[{i}]", differences);
            }
            return;
        }
        if (regulation.ValueKind is JsonValueKind.Object or JsonValueKind.Array ||
            lex.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            differences.Add($"{path} structure differs");
            return;
        }
        var a = regulation.ValueKind == JsonValueKind.String ? regulation.GetString() : regulation.ToString();
        var b = lex.ValueKind == JsonValueKind.String ? lex.GetString() : lex.ToString();
        if (!ScalarEquals(a, b))
        {
            differences.Add($"{path} regulation '{Shorten(a)}' vs lex '{Shorten(b)}'");
        }
    }

    /// <summary>
    /// Numbers are compared numerically, text ignoring case
    /// </summary>
    internal static bool ScalarEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }
        a = a.Trim();
        b = b.Trim();
        if (decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var left) &&
            decimal.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var right))
        {
            return Math.Abs(left - right) < 0.0000001m;
        }
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // helpers ----------------------------------------------------------------

    internal static string Shorten(string text) =>
        text is { Length: > 60 } ? text[..60] + "..." : text;
}
