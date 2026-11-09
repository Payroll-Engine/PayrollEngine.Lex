using System.Text;
using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class TextTableTests
{
    private static TextTableResult Read(string text, string mapJson) =>
        TextTable.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), TextTableMap.Parse(mapJson));

    [Fact]
    public void FixedColumns_WithFilter_KeepsTariffAndSpecialCodeRecords()
    {
        var result = Read(TariffSample.Text(), TariffSample.MapJson);

        Assert.Equal(TextTableFormat.FixedColumns, result.Format);
        Assert.Equal(TariffSample.FilteredCount, result.Rows.Count);
        // header, provision records, median record and trailer are filtered without warning
        Assert.Equal(5, result.Filtered);
        Assert.Equal(0, result.Ignored);
        Assert.Equal(result.Filtered + result.Rows.Count, result.Lines);
        Assert.Equal(["A0N", "A0N", "A0N", "B1Y", "B1Y", "HEN", "MEN"], result.Rows.Select(x => x.Key));
    }

    [Fact]
    public void FixedColumns_DecimalPlaces()
    {
        var result = Read(TariffSample.Text(), TariffSample.MapJson);

        var row = result.Rows[4];
        Assert.Equal(51m, row.RangeValue);
        Assert.Equal("B1Y", row.ValueObject["Code"]);
        Assert.Equal(15m, row.ValueObject["MinimumTax"]);
        Assert.Equal(0.031m, row.ValueObject["TaxRate"]);
        Assert.Null(row.Value);
        Assert.Null(row.KeyValues);
        Assert.Equal(6, row.LineNumber);

        var special = result.Rows.Single(x => x.Key == "HEN");
        Assert.Equal(1m, special.RangeValue);
        Assert.Equal(0.23m, special.ValueObject["TaxRate"]);
    }

    [Fact]
    public void WithoutFilter_RecordsWithCodeAreConverted_HeaderAndTrailerAreIgnored()
    {
        var result = Read(TariffSample.Text(), TariffSample.UnfilteredMapJson);

        // provision and median records share the layout and are converted too
        Assert.Equal(TariffSample.FilteredCount + 3, result.Rows.Count);
        Assert.Equal(0, result.Filtered);
        Assert.Equal(2, result.Ignored);
        Assert.StartsWith("00XX", result.IgnoredLines[0]);
        Assert.StartsWith("99", result.IgnoredLines[1]);
    }

    [Fact]
    public void SecondFilter_AllFiltersMustMatch()
    {
        var map = TariffSample.MapJson.Replace(
            "\"values\": [\"06\", \"11\"] }",
            "\"values\": [\"06\", \"11\"] }, { \"start\": 6, \"length\": 10, \"values\": [\"B1Y\"] }");

        var result = Read(TariffSample.Text(), map);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, x => Assert.Equal("B1Y", x.Key));
    }

    [Fact]
    public void TabDelimited_ColumnIndex()
    {
        const string map = """
            {
              "filters": [ { "start": 0, "values": ["R"] } ],
              "keys": [
                { "valueType": "text", "start": 1 },
                { "valueType": "integer", "start": 2 }
              ],
              "rangeValue": { "valueType": "decimal", "start": 3 },
              "value": { "valueType": "decimal", "start": 4, "decimalPlaces": 3 }
            }
            """;
        const string text = "H\tTitle\r\nR\tX\t2\t1000.5\t125\r\nR\tY\t\t0\tabc\r\nR\t\t\t0\t5\r\n";

        var result = Read(text, map);

        Assert.Equal(TextTableFormat.TabDelimited, result.Format);
        Assert.Equal(1, result.Filtered);
        var row = Assert.Single(result.Rows);
        Assert.Null(row.Key);
        Assert.Equal(new object[] { "X", 2 }, row.KeyValues);
        Assert.Equal(1000.5m, row.RangeValue);
        Assert.Equal("0.125", row.Value);
        // a line without value and a line without key are ignored
        Assert.Equal(2, result.Ignored);
    }

    [Fact]
    public void BooleanAndInteger_UnparsableValueIsMissing()
    {
        const string map = """
            {
              "key": { "start": 0, "length": 2 },
              "values": [
                { "name": "Active", "valueType": "boolean", "start": 2, "length": 5 },
                { "name": "Count", "valueType": "integer", "start": 7, "length": 3 }
              ]
            }
            """;

        // ReSharper disable once StringLiteralTypo
        var result = Read("K1true 012\r\nK2maybex1x\r\n", map);

        Assert.Equal(true, result.Rows[0].ValueObject["Active"]);
        Assert.Equal(12, result.Rows[0].ValueObject["Count"]);
        Assert.Single(result.Rows);
        Assert.Equal(1, result.Ignored);
    }

    [Fact]
    public void FixedColumnWithoutLength_Throws()
    {
        const string map = """{ "key": { "start": 0 }, "value": { "start": 2, "length": 1 } }""";

        Assert.Throws<FormatException>(() => Read("K1x\r\n", map));
    }

    [Fact]
    public void Map_Validate()
    {
        Assert.Empty(TextTableMap.Parse(TariffSample.MapJson).Validate());

        var errors = TextTableMap.Parse("""
            {
              "values": [ { "start": 1, "length": 2 }, { "name": "A", "start": -1, "length": 1, "decimalPlaces": 30 },
                          { "name": "A", "start": 0, "length": 1 } ],
              "filters": [ { "start": 0, "length": 2 } ]
            }
            """).Validate();

        Assert.Contains(errors, x => x.Contains("without key", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("without name", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("negative start", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("decimal places 30", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("duplicate name(s) A", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("filter without values", StringComparison.Ordinal));
        Assert.Contains(TextTableMap.Parse("{ \"key\": { \"start\": 0, \"length\": 1 } }").Validate(),
            x => x.Contains("without value", StringComparison.Ordinal));
    }

    [Fact]
    public void Map_ValueTypeIsCaseInsensitive_InvalidJsonThrows()
    {
        var map = TextTableMap.Parse("""{ "key": { "valueType": "DECIMAL", "start": 0, "length": 1 }, /* comment */ }""");

        Assert.Equal(TextValueType.Decimal, map.Key.ValueType);
        Assert.Throws<JsonException>(() => TextTableMap.Parse("{ \"key\": "));
    }
}
