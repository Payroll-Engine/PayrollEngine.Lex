using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Text table entries: reads the rows of the table, compares them with the values of the regulation lookup
/// and writes the values array. Shared by LexApply and LexCheck.
/// A row is identified by key, key values and range value; numbers are compared numerically.
/// </summary>
internal static class TableComparer
{
    /// <summary>String escaping as written by hand: umlauts, accents and symbols stay readable</summary>
    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Separator of the identity parts (not part of a JSON text)</summary>
    private const char IdentitySeparator = '\u001f';

    /// <summary>
    /// Rows of the text table of an entry
    /// </summary>
    /// <param name="entry">Entry with content file and content map</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <param name="problem">Why the table cannot be read, null on success</param>
    /// <returns>Table, null with a problem</returns>
    internal static TextTableResult Read(Entry entry, string lexDirectory, out string problem)
    {
        var source = ContentSource.Resolve(lexDirectory, entry.ContentFile);
        problem = source.Problem();
        if (problem != null)
        {
            return null;
        }
        var mapPath = ContentHash.ResolvePath(lexDirectory, entry.ContentMap);
        if (!File.Exists(mapPath))
        {
            problem = $"contentMap missing: {entry.ContentMap}";
            return null;
        }
        try
        {
            var map = TextTableMap.Load(mapPath);
            var errors = map.Validate();
            if (errors.Count > 0)
            {
                problem = $"contentMap {entry.ContentMap} is invalid: {string.Join("; ", errors)}";
                return null;
            }
            using var stream = source.Open();
            var table = TextTable.Read(stream, map);
            problem = Duplicate(table.Rows);
            return problem == null ? table : null;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or IOException or InvalidDataException)
        {
            problem = $"text table {entry.ContentFile} cannot be read: {exception.Message}";
            return null;
        }
    }

    /// <summary>
    /// Compares the rows with the values of the lookup
    /// </summary>
    internal static TableDifference Compare(List<TextTableRow> rows, RegulationLookup lookup)
    {
        // a regulation identity may occur more than once: each row takes one value, the rest is removed
        var values = new Dictionary<string, Queue<RegulationValue>>(StringComparer.Ordinal);
        foreach (var value in lookup.Values)
        {
            var identity = Identity(value);
            if (!values.TryGetValue(identity, out var queue))
            {
                values[identity] = queue = new Queue<RegulationValue>();
            }
            queue.Enqueue(value);
        }
        var difference = new TableDifference();
        foreach (var row in rows)
        {
            if (!values.TryGetValue(Identity(row), out var queue) || queue.Count == 0)
            {
                difference.Added++;
                continue;
            }
            var value = queue.Dequeue();
            if (ValueComparer.Difference(value.Object, value.Scalar, Content(row)) == null)
            {
                difference.Unchanged++;
            }
            else
            {
                difference.Changed++;
            }
        }
        difference.Removed = values.Values.Sum(x => x.Count);
        return difference;
    }

    /// <summary>
    /// JSON text of the values array: one value per line (key, keyValues, rangeValue, value, valueObject),
    /// numbers in invariant notation without trailing zeros
    /// </summary>
    /// <param name="rows">Table rows</param>
    /// <param name="indent">Indentation of the line with the values property</param>
    /// <param name="newLine">Line break of the file</param>
    internal static string ValuesText(List<TextTableRow> rows, string indent, string newLine)
    {
        if (rows.Count == 0)
        {
            return "[]";
        }
        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < rows.Count; i++)
        {
            builder.Append(newLine).Append(indent).Append("  ");
            AppendValue(builder, rows[i]);
            if (i < rows.Count - 1)
            {
                builder.Append(',');
            }
        }
        builder.Append(newLine).Append(indent).Append(']');
        return builder.ToString();
    }

    /// <summary>
    /// First duplicate row identity, null without
    /// </summary>
    private static string Duplicate(List<TextTableRow> rows)
    {
        var lines = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var identity = Identity(row);
            if (!lines.TryAdd(identity, row.LineNumber))
            {
                return $"duplicate table row {string.Join(' ', identity.Split(IdentitySeparator).Where(x => x.Length > 0))} " +
                       $"(lines {lines[identity]} and {row.LineNumber})";
            }
        }
        return null;
    }

    /// <summary>
    /// Value object of a row
    /// </summary>
    private static void AppendValue(StringBuilder builder, TextTableRow row)
    {
        var properties = new List<string>();
        if (row.Key != null)
        {
            properties.Add("\"key\": " + JsonString(row.Key));
        }
        if (row.KeyValues != null)
        {
            properties.Add("\"keyValues\": [" + string.Join(", ", row.KeyValues.Select(Literal)) + "]");
        }
        if (row.RangeValue != null)
        {
            properties.Add("\"rangeValue\": " + Number(row.RangeValue.Value));
        }
        if (row.Value != null)
        {
            properties.Add("\"value\": " + JsonString(row.Value));
        }
        if (row.ValueObject != null)
        {
            properties.Add("\"valueObject\": " + ObjectText(row.ValueObject));
        }
        builder.Append("{ ").Append(string.Join(", ", properties)).Append(" }");
    }

    /// <summary>
    /// Compared content of a row: the value object, else the value
    /// </summary>
    private static string Content(TextTableRow row) =>
        row.ValueObject != null ? ObjectText(row.ValueObject) : row.Value;

    /// <summary>
    /// JSON object of the named values, in the order of the map
    /// </summary>
    private static string ObjectText(Dictionary<string, object> values) =>
        "{ " + string.Join(", ", values.Select(x => JsonString(x.Key) + ": " + Literal(x.Value))) + " }";

    /// <summary>
    /// Identity of a row: key, key values and range value
    /// </summary>
    private static string Identity(TextTableRow row) =>
        string.Join(IdentitySeparator,
            row.Key ?? string.Empty,
            row.KeyValues == null ? string.Empty : "[" + string.Join(",", row.KeyValues.Select(Literal)) + "]",
            row.RangeValue == null ? string.Empty : Number(row.RangeValue.Value));

    /// <summary>
    /// Identity of a regulation value, in the form of <see cref="Identity(TextTableRow)"/>
    /// </summary>
    private static string Identity(RegulationValue value) =>
        string.Join(IdentitySeparator,
            value.ValueKey ?? string.Empty,
            value.KeyValues == null
                ? string.Empty
                : "[" + string.Join(",", value.KeyValues.Value.EnumerateArray().Select(Literal)) + "]",
            value.RangeValue == null ? string.Empty : NumberText(value.RangeValue));

    /// <summary>
    /// JSON literal of a converted table value
    /// </summary>
    private static string Literal(object value) =>
        value switch
        {
            null => "null",
            string text => JsonString(text),
            decimal number => Number(number),
            int number => number.ToString(CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => JsonString(Convert.ToString(value, CultureInfo.InvariantCulture))
        };

    /// <summary>
    /// JSON literal of a regulation element, numbers in the notation of <see cref="Number"/>
    /// </summary>
    private static string Literal(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => JsonString(element.GetString()),
            JsonValueKind.Number => NumberText(element.GetRawText()),
            _ => element.GetRawText()
        };

    /// <summary>
    /// Number text in the notation of <see cref="Number"/>, other text unchanged
    /// </summary>
    private static string NumberText(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Number(number) : text;

    /// <summary>
    /// Invariant notation without trailing zeros (1.00 is written as 1, 0.0125 stays 0.0125)
    /// </summary>
    private static string Number(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// JSON string literal of a text
    /// </summary>
    private static string JsonString(string text) => JsonSerializer.Serialize(text, StringOptions);
}
