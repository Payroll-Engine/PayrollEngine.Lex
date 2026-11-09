using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace PayrollEngine.Lex.Tests;

/// <summary>
/// Fixed-column tariff text table with invented values: header record 00, tariff records 06,
/// special code records 11, provision records 12, median record 13 and trailer record 99
/// </summary>
internal static class TariffSample
{
    /// <summary>Map of the tariff records 06 and 11 (code, income from, minimum tax, rate)</summary>
    internal const string MapJson = """
        {
          "filters": [
            { "start": 0, "length": 2, "values": ["06", "11"] }
          ],
          "key":        { "valueType": "text",    "start": 6,  "length": 10 },
          "rangeValue": { "valueType": "decimal", "start": 24, "length": 9, "decimalPlaces": 2 },
          "values": [
            { "name": "Code",       "valueType": "text",    "start": 6,  "length": 10 },
            { "name": "MinimumTax", "valueType": "decimal", "start": 45, "length": 9, "decimalPlaces": 2 },
            { "name": "TaxRate",    "valueType": "decimal", "start": 54, "length": 5, "decimalPlaces": 4 }
          ]
        }
        """;

    /// <summary>Map without filters (all records with a code)</summary>
    internal const string UnfilteredMapJson = """
        {
          "key":        { "valueType": "text",    "start": 6,  "length": 10 },
          "rangeValue": { "valueType": "decimal", "start": 24, "length": 9, "decimalPlaces": 2 },
          "values": [
            { "name": "Code",       "valueType": "text",    "start": 6,  "length": 10 },
            { "name": "MinimumTax", "valueType": "decimal", "start": 45, "length": 9, "decimalPlaces": 2 },
            { "name": "TaxRate",    "valueType": "decimal", "start": 54, "length": 5, "decimalPlaces": 4 }
          ]
        }
        """;

    /// <summary>Tariff records: code, income from, step, minimum tax, rate (raw values with implied decimals)</summary>
    internal static readonly (string Code, int From, int Step, int MinimumTax, int Rate)[] Tariffs =
    [
        ("A0N", 100, 5000, 0, 0),
        ("A0N", 5100, 5000, 0, 125),
        ("A0N", 10100, 5000, 0, 250),
        ("B1Y", 100, 5000, 0, 0),
        ("B1Y", 5100, 5000, 1500, 310)
    ];

    /// <summary>Special codes (record 11)</summary>
    internal static readonly (string Code, int Rate)[] SpecialCodes = [("HEN", 2300), ("MEN", 2950)];

    /// <summary>Number of converted values with the filtered map</summary>
    internal static int FilteredCount => Tariffs.Length + SpecialCodes.Length;

    /// <summary>
    /// Text of the table, optionally with changed tariff records
    /// </summary>
    internal static string Text(IEnumerable<(string Code, int From, int Step, int MinimumTax, int Rate)> tariffs = null)
    {
        var lines = new List<string> { "00XX".PadRight(19) + "20260101".PadRight(90) };
        lines.AddRange((tariffs ?? Tariffs).Select(x => Line("06", x.Code, x.From, x.Step, x.MinimumTax, x.Rate)));
        lines.AddRange(SpecialCodes.Select(x => Line("11", x.Code, 100, 99999900, 0, x.Rate)));
        lines.Add(Line("12", "PEL", 100, 99999900, 0, 200));
        lines.Add(Line("12", "PPA", 100, 99999900, 0, 100));
        lines.Add(Line("13", "MED", 100, 99999900, 567500, 0));
        lines.Add("99".PadRight(17) + "XX" + (lines.Count + 1).ToString("00000000", CultureInfo.InvariantCulture).PadRight(22));
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// Record line: type, transaction 01, region XX, code (10), date (8), income from (9), step (9),
    /// blank, children (2), minimum tax (9), rate (5)
    /// </summary>
    internal static string Line(string record, string code, int from, int step, int minimumTax, int rate) =>
        record + "01XX" + code.PadRight(10) + "20260101" +
        from.ToString("000000000", CultureInfo.InvariantCulture) +
        step.ToString("000000000", CultureInfo.InvariantCulture) + " 00" +
        minimumTax.ToString("000000000", CultureInfo.InvariantCulture) +
        rate.ToString("00000", CultureInfo.InvariantCulture) + "   ";

    /// <summary>
    /// Writes the table into a ZIP archive (with a second table entry)
    /// </summary>
    internal static void WriteArchive(string path, string entryName, string text)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in new[] { (entryName, text), ("other.txt", "00YY\r\n") })
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
    }
}
