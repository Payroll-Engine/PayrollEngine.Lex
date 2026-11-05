using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ExcelDataReader;

namespace PayrollEngine.Lex;

/// <summary>
/// Reads the text of a spreadsheet (xls, xlsx, xlsb), one section per sheet
/// </summary>
internal static class SpreadsheetReader
{
    /// <summary>Maximum number of rows read per sheet, counted from the first row with a value</summary>
    private const int MaxRows = 5000;

    static SpreadsheetReader()
    {
        // code pages of the binary xls format
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Text of a spreadsheet: section heading is the sheet name, one line per row,
    /// numbers in invariant format, dates as serial number
    /// </summary>
    /// <param name="stream">The spreadsheet stream (the format is detected from the content)</param>
    internal static List<(string Heading, string Text)> Read(Stream stream)
    {
        var sections = new List<(string, string)>();
        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            var text = new StringBuilder();
            int? firstRow = null;
            var rowIndex = -1;
            while (reader.Read())
            {
                rowIndex++;
                if (firstRow.HasValue && rowIndex > firstRow.Value + MaxRows)
                {
                    break;
                }
                var row = new StringBuilder();
                var hasValue = false;
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    if (reader.IsDBNull(column))
                    {
                        continue;
                    }
                    hasValue = true;
                    var cellText = CellText(reader.GetValue(column));
                    if (cellText != null)
                    {
                        row.Append(cellText).Append(' ');
                    }
                }
                if (!hasValue)
                {
                    continue;
                }
                firstRow ??= rowIndex;
                text.Append(row).Append('\n');
            }
            sections.Add((reader.Name, text.ToString()));
        } while (reader.NextResult());
        return sections;
    }

    private static string CellText(object value) =>
        value switch
        {
            string text => text,
            DateTime date => Number(date.ToOADate()),
            TimeSpan time => Number(time.TotalDays),
            double or float or decimal or int or long or short or byte => Number(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            // boolean and error values hold no text
            _ => null
        };

    private static string Number(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
