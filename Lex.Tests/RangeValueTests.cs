using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

/// <summary>New rows of range lookups: range value derived from a date key, explicit range value, errors,
/// idempotence and the check finding</summary>
public class RangeValueTests
{
    private const string Lookups = """
        [
          {
            "name": "Thresholds",
            "rangeMode": "Threshold",
            "values": [
              { "key": "2026-01-01", "rangeValue": 20260101, "value": "13.9" }
            ]
          },
          {
            "name": "Plain",
            "values": [
              { "key": "A", "value": "1" }
            ]
          }
        ]
        """;

    private static TestSatellite Satellite(string lookups = Lookups)
    {
        var s = new TestSatellite();
        s.WriteRegulation(lookups);
        return s;
    }

    private static string Read(TestSatellite s) => File.ReadAllText(Path.Combine(s.Root, "Regulation", "Test.json"));

    private static List<JsonElement> Rows(TestSatellite s, string lookup)
    {
        using var document = JsonDocument.Parse(Read(s));
        return document.RootElement.GetProperty("tenants")[0].GetProperty("regulations")[0].GetProperty("lookups")
            .EnumerateArray().Single(x => x.GetProperty("name").GetString() == lookup)
            .GetProperty("values").EnumerateArray().Select(x => x.Clone()).ToList();
    }

    [Fact]
    public void NewRow_WithDateKey_GetsRangeValueFromKey()
    {
        using var s = Satellite();
        var manifest = TestSatellite.Manifest(
            TestSatellite.Value("Thresholds.2026-01-01", "13.9"),
            TestSatellite.Value("Thresholds.2025-01-01", "12.8"));

        var result = Applier.Apply(manifest, s.LexPath, dryRun: false);

        Assert.Null(result.Error);
        var change = Assert.Single(result.Changes);
        Assert.Equal(ApplyChangeKind.Added, change.Kind);
        Assert.Equal(20250101m, change.RangeValue);
        Assert.Contains("rangeValue 20250101", change.Detail, StringComparison.Ordinal);
        var rows = Rows(s, "Thresholds");
        Assert.Equal(2, rows.Count);
        Assert.Equal("2025-01-01", rows[1].GetProperty("key").GetString());
        Assert.Equal(20250101, rows[1].GetProperty("rangeValue").GetInt32());
        Assert.Equal("12.8", rows[1].GetProperty("value").GetString());
        // key, rangeValue, value: the order of the existing rows
        Assert.Equal(["key", "rangeValue", "value"], rows[1].EnumerateObject().Select(x => x.Name).ToArray());
    }

    [Fact]
    public void NewRow_StructuredValue_GetsRangeValue()
    {
        using var s = Satellite("""
            [ { "name": "Thresholds", "rangeMode": "Threshold",
                "values": [ { "key": "2026-01-01", "rangeValue": 20260101, "valueObject": { "Min": 13.9 } } ] } ]
            """);
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Thresholds.2025-01-01", "{\"Min\":12.8}"));

        var result = Applier.Apply(manifest, s.LexPath, dryRun: false);

        Assert.Null(result.Error);
        var rows = Rows(s, "Thresholds");
        Assert.Equal(20250101, rows[1].GetProperty("rangeValue").GetInt32());
        Assert.Equal(12.8, rows[1].GetProperty("valueObject").GetProperty("Min").GetDouble());
    }

    [Fact]
    public void DryRun_ShowsRangeValue_AndWritesNothing()
    {
        using var s = Satellite();
        var before = Read(s);

        var result = Applier.Apply(TestSatellite.Manifest(TestSatellite.Value("Thresholds.2025-01-01", "12.8")),
            s.LexPath, dryRun: true);

        Assert.Equal(20250101m, Assert.Single(result.Changes).RangeValue);
        Assert.Empty(result.WrittenFiles);
        Assert.Equal(before, Read(s));
    }

    [Fact]
    public void SecondApply_ChangesNothing()
    {
        using var s = Satellite();
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Thresholds.2025-01-01", "12.8"));

        Applier.Apply(manifest, s.LexPath, dryRun: false);
        var text = Read(s);
        var second = Applier.Apply(manifest, s.LexPath, dryRun: false);

        Assert.Empty(second.Changes);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(text, Read(s));
    }

    [Fact]
    public void ExistingRow_KeepsItsRangeValue()
    {
        // a hand-set range value of an existing row is never recomputed, also not when it differs from the key
        using var s = Satellite("""
            [ { "name": "Thresholds", "rangeMode": "Threshold",
                "values": [ { "key": "2026-01-01", "rangeValue": 12345, "value": "13.9" } ] } ]
            """);

        var result = Applier.Apply(TestSatellite.Manifest(TestSatellite.Value("Thresholds.2026-01-01", "14")),
            s.LexPath, dryRun: false);

        Assert.Null(result.Error);
        var row = Assert.Single(Rows(s, "Thresholds"));
        Assert.Equal(12345, row.GetProperty("rangeValue").GetInt32());
        Assert.Equal("14", row.GetProperty("value").GetString());
    }

    [Fact]
    public void NewRow_WithOtherKey_IsRangeValueMissing_AndNothingIsWritten()
    {
        using var s = Satellite();
        var before = Read(s);
        var manifest = TestSatellite.Manifest(
            TestSatellite.Value("Thresholds.Base", "1"),
            TestSatellite.Value("Plain.B", "2"));

        var result = Applier.Apply(manifest, s.LexPath, dryRun: false);

        Assert.StartsWith(Applier.RangeValueMissingCode + ":", result.Error, StringComparison.Ordinal);
        Assert.Contains("Thresholds.Base", result.Error, StringComparison.Ordinal);
        Assert.Single(result.RangeValueMissing);
        Assert.Empty(result.WrittenFiles);
        // the other new row is not written either: the action fails as a whole
        Assert.Equal(before, Read(s));
    }

    [Theory]
    [InlineData("2025-13-01")]
    [InlineData("2025-1-1")]
    [InlineData("20250101")]
    public void NewRow_WithMalformedDate_IsRangeValueMissing(string key)
    {
        using var s = Satellite();

        var result = Applier.Apply(TestSatellite.Manifest(TestSatellite.Value("Thresholds." + key, "1")),
            s.LexPath, dryRun: true);

        Assert.Single(result.RangeValueMissing);
        Assert.StartsWith(Applier.RangeValueMissingCode, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void NewRow_WithExplicitRangeValue_UsesIt()
    {
        using var s = Satellite();
        var entry = new Entry
        {
            Key = "Thresholds.Base",
            Content = "1",
            RangeValue = 0,
            SourceUrls = ["https://example.org/doc"],
            SourceLocation = "p. 1",
            LegalReference = "Test Act s. 1"
        };

        var result = Applier.Apply(TestSatellite.Manifest(entry), s.LexPath, dryRun: false);

        Assert.Null(result.Error);
        var rows = Rows(s, "Thresholds");
        Assert.Equal("Base", rows[1].GetProperty("key").GetString());
        Assert.Equal(0, rows[1].GetProperty("rangeValue").GetInt32());
    }

    [Fact]
    public void NewRow_InLookupWithoutRangeMode_HasNoRangeValue()
    {
        using var s = Satellite();

        var result = Applier.Apply(TestSatellite.Manifest(TestSatellite.Value("Plain.2025-01-01", "2")),
            s.LexPath, dryRun: false);

        Assert.Null(result.Error);
        Assert.Null(Assert.Single(result.Changes).RangeValue);
        Assert.False(Rows(s, "Plain")[1].TryGetProperty("rangeValue", out _));
    }

    [Fact]
    public void RangeModeNone_IsNoRangeLookup()
    {
        using var s = Satellite("""
            [ { "name": "Thresholds", "rangeMode": "None", "values": [ { "key": "A", "value": "1" } ] } ]
            """);

        var result = Applier.Apply(TestSatellite.Manifest(TestSatellite.Value("Thresholds.B", "2")),
            s.LexPath, dryRun: false);

        Assert.Null(result.Error);
        Assert.False(Rows(s, "Thresholds")[1].TryGetProperty("rangeValue", out _));
    }

    [Fact]
    public void AppliedRow_PassesTheReleaseCheck_WithoutRangeFinding()
    {
        using var s = Satellite();
        var manifest = TestSatellite.Manifest(
            TestSatellite.Value("Thresholds.2026-01-01", "13.9"),
            TestSatellite.Value("Thresholds.2025-01-01", "12.8"),
            TestSatellite.Value("Plain.A", "1"));
        Applier.Apply(manifest, s.LexPath, dryRun: false);

        var check = Checker.Check(manifest, s.LexPath, release: true);

        Assert.Equal(0, check.RangeValueMissing);
        Assert.Equal(0, check.Drift);
    }

    [Fact]
    public void Check_RowWithoutRangeValue_IsReleaseFinding()
    {
        using var s = Satellite("""
            [ { "name": "Thresholds", "rangeMode": "Threshold",
                "values": [ { "key": "2026-01-01", "rangeValue": 20260101, "value": "13.9" },
                            { "key": "2025-01-01", "value": "12.8" } ] },
              { "name": "Plain", "values": [ { "key": "A", "value": "1" } ] } ]
            """);
        var manifest = TestSatellite.Manifest(
            TestSatellite.Value("Thresholds.2026-01-01", "13.9"),
            TestSatellite.Value("Thresholds.2025-01-01", "12.8"),
            TestSatellite.Value("Plain.A", "1"));

        var development = Checker.Check(manifest, s.LexPath);
        var release = Checker.Check(manifest, s.LexPath, release: true);

        Assert.Equal(1, development.RangeValueMissing);
        Assert.Equal(["Thresholds.2025-01-01"], development.RangeValueMissingSamples);
        // the missing range value is a finding only in the release mode
        Assert.DoesNotContain("rangeValue", string.Join(" ", development.ValidationErrors), StringComparison.Ordinal);
        Assert.Equal(1, release.RangeValueMissing);
        Assert.True(release.HasFindings);
        Assert.Contains("| Range lookup values without rangeValue | 1 |", CheckReport.BuildMarkdown([release]), StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_AddEntry_WritesRangeValueAfterContent()
    {
        var editor = ManifestEditor.Parse("""{ "authority": "XX", "values": [] }""");

        editor.AddValue(new Entry
        {
            Key = "Thresholds.Base", Content = "1", RangeValue = 0, SourceUrls = ["https://example.org/doc"],
            LegalReference = "Test Act s. 1"
        });

        using var document = JsonDocument.Parse(editor.ToJson());
        var entry = document.RootElement.GetProperty("values")[0];
        Assert.Equal(["key", "content", "rangeValue", "sourceUrls", "sourceLocation", "legalReference"],
            entry.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.Equal(0, entry.GetProperty("rangeValue").GetInt32());
    }

    [Fact]
    public void Editor_UpdateEntry_SetsChangesAndRemovesRangeValue()
    {
        var editor = ManifestEditor.Parse("""
            { "values": [ { "key": "Thresholds.Base", "content": "1", "sourceUrls": [ "https://example.org/doc" ],
                            "legalReference": "Test Act s. 1" } ] }
            """);

        Assert.Equal(EntryUpdateResult.Updated, editor.UpdateEntry("Thresholds.Base", new EntryUpdate { RangeValue = 20250101 }));
        Assert.Equal(EntryUpdateResult.Unchanged, editor.UpdateEntry("Thresholds.Base", new EntryUpdate { RangeValue = 20250101 }));
        Assert.Equal(EntryUpdateResult.Updated, editor.UpdateEntry("Thresholds.Base", new EntryUpdate { RangeValue = 0 }));

        using (var document = JsonDocument.Parse(editor.ToJson()))
        {
            var entry = document.RootElement.GetProperty("values")[0];
            Assert.Equal(["key", "content", "rangeValue", "sourceUrls", "legalReference"],
                entry.EnumerateObject().Select(x => x.Name).ToArray());
            Assert.Equal(0, entry.GetProperty("rangeValue").GetInt32());
        }

        Assert.Equal(EntryUpdateResult.Updated, editor.UpdateEntry("Thresholds.Base", new EntryUpdate { RemoveRangeValue = true }));
        Assert.DoesNotContain("rangeValue", editor.ToJson(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            editor.UpdateEntry("Thresholds.Base", new EntryUpdate { RangeValue = 1, RemoveRangeValue = true }));
    }

    [Fact]
    public void RangeValue_DoesNotInvalidateTheVerification()
    {
        Assert.False(new EntryUpdate { RangeValue = 20250101 }.InvalidatesVerification);
        Assert.False(new EntryUpdate { RemoveRangeValue = true }.InvalidatesVerification);
    }

    [Fact]
    public void Manifest_ReadsRangeValueOfAnEntry()
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(
            """{ "values": [ { "key": "Thresholds.Base", "content": "1", "rangeValue": 20250101 } ] }""",
            LexJson.ManifestOptions);

        Assert.Equal(20250101m, manifest!.Values[0].RangeValue);
    }
}
