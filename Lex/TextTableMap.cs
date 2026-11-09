using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Mapping of text table lines to lookup values: key or composite keys, optional range value,
/// a scalar value or the properties of a value object, and optional line filters
/// </summary>
public sealed class TextTableMap
{
    /// <summary>Tolerant reading of handwritten map files</summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Highest number of implied decimal places of a decimal value</summary>
    private const int MaxDecimalPlaces = 28;

    /// <summary>
    /// Lookup value key
    /// </summary>
    public TextTableField Key { get; set; }

    /// <summary>
    /// Composite key, written to the key values of the lookup value
    /// </summary>
    public List<TextTableField> Keys { get; set; }

    /// <summary>
    /// Range value of a range lookup
    /// </summary>
    public TextTableField RangeValue { get; set; }

    /// <summary>
    /// Scalar lookup value
    /// </summary>
    public TextTableField Value { get; set; }

    /// <summary>
    /// Properties of the lookup value object
    /// </summary>
    public List<TextTableField> Values { get; set; }

    /// <summary>
    /// Line filters, all must match; lines that do not match are skipped without warning
    /// </summary>
    public List<TextTableFilter> Filters { get; set; }

    /// <summary>
    /// Parse a map from JSON
    /// </summary>
    /// <param name="json">Map JSON</param>
    /// <exception cref="JsonException">Invalid JSON</exception>
    public static TextTableMap Parse(string json) =>
        JsonSerializer.Deserialize<TextTableMap>(json, ReadOptions) ?? new TextTableMap();

    /// <summary>
    /// Load a map from a JSON file
    /// </summary>
    /// <param name="path">Map file</param>
    /// <exception cref="JsonException">Invalid JSON</exception>
    public static TextTableMap Load(string path) =>
        Parse(File.ReadAllText(path));

    /// <summary>
    /// Structural errors of the map (empty = valid)
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (Key == null && (Keys == null || Keys.Count == 0))
        {
            errors.Add("map without key mapping (key or keys)");
        }
        if (Value == null && (Values == null || Values.Count == 0))
        {
            errors.Add("map without value mapping (value or values)");
        }
        ValidateField(errors, "key", Key);
        foreach (var field in Keys ?? [])
        {
            ValidateField(errors, "keys", field);
        }
        ValidateField(errors, "rangeValue", RangeValue);
        ValidateField(errors, "value", Value);
        foreach (var field in Values ?? [])
        {
            // the name is the property of the value object
            if (string.IsNullOrWhiteSpace(field?.Name))
            {
                errors.Add($"values: field without name {field}");
            }
            ValidateField(errors, "values", field);
        }
        var duplicates = (Values ?? []).Where(x => !string.IsNullOrWhiteSpace(x?.Name))
            .GroupBy(x => x.Name).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
        if (duplicates.Count > 0)
        {
            errors.Add($"values: duplicate name(s) {string.Join(", ", duplicates)}");
        }
        foreach (var filter in Filters ?? [])
        {
            if (filter == null)
            {
                errors.Add("filters: empty filter");
                continue;
            }
            if (filter.Start < 0 || filter.Length < 0)
            {
                errors.Add($"filters: negative start or length {filter}");
            }
            if (filter.Values == null || filter.Values.Count == 0)
            {
                errors.Add($"filters: filter without values {filter}");
            }
        }
        return errors;
    }

    /// <summary>
    /// Position and decimal places of a field
    /// </summary>
    private static void ValidateField(List<string> errors, string section, TextTableField field)
    {
        if (field == null)
        {
            return;
        }
        if (field.Start < 0 || field.Length < 0)
        {
            errors.Add($"{section}: negative start or length {field}");
        }
        if (field.DecimalPlaces is < 0 or > MaxDecimalPlaces)
        {
            errors.Add($"{section}: decimal places {field.DecimalPlaces} out of range 0-{MaxDecimalPlaces} {field}");
        }
    }
}
