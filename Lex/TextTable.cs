using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Converts the lines of a text table (fixed columns or tab-delimited) into lookup values
/// according to a <see cref="TextTableMap"/>
/// </summary>
public static class TextTable
{
    /// <summary>
    /// Read the lookup values of a text table
    /// </summary>
    /// <param name="stream">Text table (UTF-8, a byte order mark selects the encoding)</param>
    /// <param name="map">Valid map (see <see cref="TextTableMap.Validate"/>)</param>
    /// <exception cref="FormatException">A fixed column of the map has no length</exception>
    public static TextTableResult Read(Stream stream, TextTableMap map)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, leaveOpen: true);
        return Read(reader, map);
    }

    /// <summary>
    /// Read the lookup values of a text table
    /// </summary>
    /// <param name="reader">Text table reader</param>
    /// <param name="map">Valid map (see <see cref="TextTableMap.Validate"/>)</param>
    /// <exception cref="FormatException">A fixed column of the map has no length</exception>
    public static TextTableResult Read(TextReader reader, TextTableMap map)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(map);
        var result = new TextTableResult();
        var format = TextTableFormat.FixedColumns;
        while (reader.ReadLine() is { } line)
        {
            result.Lines++;
            // the format is taken from the lines until the first one is converted (header lines may differ)
            if (result.Rows.Count == 0)
            {
                format = line.Contains('\t') ? TextTableFormat.TabDelimited : TextTableFormat.FixedColumns;
                result.Format = format;
            }
            if (!Accepted(line, map.Filters, format))
            {
                result.Filtered++;
                continue;
            }
            var row = ConvertLine(line, result.Lines, map, format);
            if (row == null)
            {
                result.IgnoredLines.Add(line);
                continue;
            }
            result.Rows.Add(row);
        }
        return result;
    }

    /// <summary>
    /// All filters of the map match the line
    /// </summary>
    private static bool Accepted(string line, List<TextTableFilter> filters, TextTableFormat format)
    {
        if (filters == null)
        {
            return true;
        }
        foreach (var filter in filters)
        {
            var text = Extract(line, filter.Start, filter.Length, format, "filter");
            if (text == null || filter.Values == null || !filter.Values.Contains(text, StringComparer.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Lookup value of a line, null without key or without value
    /// </summary>
    private static TextTableRow ConvertLine(string line, int lineNumber, TextTableMap map, TextTableFormat format)
    {
        var key = ConvertValue(line, map.Key, format);
        var keyValues = new List<object>();
        foreach (var field in map.Keys ?? [])
        {
            var keyValue = ConvertValue(line, field, format);
            if (keyValue != null)
            {
                keyValues.Add(keyValue);
            }
        }
        if (key == null && keyValues.Count == 0)
        {
            return null;
        }

        var value = ConvertValue(line, map.Value, format);
        var valueObject = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var field in map.Values ?? [])
        {
            var objectValue = ConvertValue(line, field, format);
            if (objectValue != null)
            {
                valueObject[field.Name] = objectValue;
            }
        }
        if (value == null && valueObject.Count == 0)
        {
            return null;
        }

        return new TextTableRow
        {
            LineNumber = lineNumber,
            Key = key == null ? null : Convert.ToString(key, CultureInfo.InvariantCulture),
            KeyValues = keyValues.Count > 0 ? keyValues : null,
            RangeValue = RangeValue(ConvertValue(line, map.RangeValue, format)),
            Value = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture),
            ValueObject = valueObject.Count > 0 ? valueObject : null
        };
    }

    /// <summary>
    /// Range value of a converted column: numbers as they are, text parsed invariant
    /// </summary>
    private static decimal? RangeValue(object value) =>
        value switch
        {
            decimal number => number,
            int number => number,
            string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };

    /// <summary>
    /// Typed value of a column, null for an empty or unparsable column
    /// </summary>
    private static object ConvertValue(string line, TextTableField field, TextTableFormat format)
    {
        if (field == null)
        {
            return null;
        }
        var text = Extract(line, field.Start, field.Length, format, field.Name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        return field.ValueType switch
        {
            TextValueType.Text => text,
            TextValueType.Decimal => ConvertDecimal(text, field.DecimalPlaces),
            TextValueType.Integer => int.TryParse(text, CultureInfo.InvariantCulture, out var integer) ? integer : null,
            TextValueType.Boolean => bool.TryParse(text, out var boolean) ? boolean : null,
            _ => null
        };
    }

    /// <summary>
    /// Decimal value with implied decimal places: the raw number is divided by 10^n
    /// </summary>
    private static decimal? ConvertDecimal(string text, int decimalPlaces)
    {
        if (!decimal.TryParse(text, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }
        return decimalPlaces == 0 ? number : number / (decimal)Math.Pow(10, decimalPlaces);
    }

    /// <summary>
    /// Trimmed text of a column, null if the line is empty or shorter than the column start
    /// </summary>
    private static string Extract(string line, int start, int length, TextTableFormat format, string name)
    {
        if (string.IsNullOrWhiteSpace(line) || start >= line.Length)
        {
            return null;
        }
        if (format == TextTableFormat.TabDelimited)
        {
            var tokens = line.Split('\t');
            return start < tokens.Length ? tokens[start].Trim() : null;
        }
        if (length == 0)
        {
            throw new FormatException($"Missing length in fixed column mapping {name}.");
        }
        return line.Substring(start, Math.Min(length, line.Length - start)).Trim();
    }
}
