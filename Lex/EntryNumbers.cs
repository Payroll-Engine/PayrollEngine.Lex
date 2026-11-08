using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Numbers of a lex entry content that are looked up in the sources
/// </summary>
internal static class EntryNumbers
{
    private const int MaxNumbers = 40;
    /// <summary>
    /// Numbers of an entry content (scalars, object properties, array items), sorted; 0, 1 and open upper bounds
    /// (nines only, see <see cref="IsPlaceholder"/>) carry no information
    /// </summary>
    internal static List<decimal> ContentNumbers(string content) =>
        ContentNumbers(content, [], out _);

    /// <summary>
    /// Numbers of an entry content without the numbers of the derived properties; a number that is also
    /// in a property that is not derived remains
    /// </summary>
    /// <param name="content">Entry content</param>
    /// <param name="derived">Property paths of the derived properties</param>
    /// <param name="derivedNumbers">Numbers of the derived properties</param>
    internal static List<decimal> ContentNumbers(string content, IReadOnlyCollection<string> derived, out int derivedNumbers)
    {
        derivedNumbers = 0;
        var numbers = new HashSet<decimal>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(content);
            if (derived.Count == 0)
            {
                Collect(document.RootElement, numbers);
            }
            else
            {
                var derivedValues = new HashSet<decimal>();
                Collect(document.RootElement, numbers, string.Empty,
                    derived.ToHashSet(StringComparer.OrdinalIgnoreCase), derivedValues);
                derivedNumbers = derivedValues.Count;
            }
        }
        catch (JsonException)
        {
            Add(numbers, content);
        }
        return numbers.OrderBy(x => x).ToList();
    }

    /// <summary>
    /// Deterministic sample of sorted numbers: all numbers up to the maximum, otherwise numbers evenly
    /// distributed over the whole range, including the smallest and the largest
    /// </summary>
    internal static List<decimal> Sample(List<decimal> numbers, int max = MaxNumbers)
    {
        if (numbers.Count <= max)
        {
            return numbers;
        }
        var sample = new List<decimal>(max);
        for (var i = 0; i < max; i++)
        {
            sample.Add(numbers[(int)((long)i * (numbers.Count - 1) / (max - 1))]);
        }
        return sample;
    }

    /// <summary>
    /// Collect the numbers of a JSON element; the numbers of a derived property go to <paramref name="derivedNumbers"/>.
    /// The property path runs over objects only (null below an array: no derived property).
    /// </summary>
    private static void Collect(JsonElement element, HashSet<decimal> numbers, string path = null,
        HashSet<string> derived = null, HashSet<decimal> derivedNumbers = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var value))
                {
                    Consider(numbers, value);
                }
                break;
            case JsonValueKind.String:
                Add(numbers, element.GetString());
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var propertyPath = path == null ? null : path.Length == 0 ? property.Name : path + "." + property.Name;
                    if (propertyPath != null && derived != null && derived.Contains(propertyPath))
                    {
                        Collect(property.Value, derivedNumbers);
                        continue;
                    }
                    Collect(property.Value, numbers, propertyPath, derived, derivedNumbers);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, numbers);
                }
                break;
        }
    }

    private static void Add(HashSet<decimal> numbers, string text)
    {
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            Consider(numbers, value);
        }
    }

    private static void Consider(HashSet<decimal> numbers, decimal value)
    {
        value = Math.Abs(value);
        // ReSharper disable once MergeIntoPattern
        if ((value > 1m || (value > 0m && value < 1m)) && !IsPlaceholder(value))
        {
            numbers.Add(value);
        }
    }

    private const int MinPlaceholderDigits = 6;

    /// <summary>
    /// Open upper bound of a range, written as nines only (999999, 99999999.99): not a value of the source
    /// </summary>
    internal static bool IsPlaceholder(decimal value)
    {
        var whole = Math.Truncate(value).ToString(CultureInfo.InvariantCulture);
        if (whole.Length < MinPlaceholderDigits || whole.Any(c => c != '9'))
        {
            return false;
        }
        var fraction = value.ToString(CultureInfo.InvariantCulture);
        var point = fraction.IndexOf('.');
        if (point < 0)
        {
            return true;
        }
        fraction = fraction[(point + 1)..];
        return fraction.All(c => c == '9') || fraction.All(c => c == '0');
    }
}
