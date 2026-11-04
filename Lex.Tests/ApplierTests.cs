using System.Text;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ApplierTests
{
    private const string Regulation = """
        {
          "tenants": [
            {
              "regulations": [
                {
                  "name": "Test.Regulation",
                  "validFrom": "2026-01-01T00:00:00Z",
                  "lookups": [
                    {
                      "name": "Rates",
                      "description": "Rates by code",
                      "values": [
                        { "key": "A", "description": "Rate A", "value": "8.5" },
                        { "key": "B", "value": "10" }
                      ]
                    },
                    {
                      "name": "Limits",
                      "values": [
                        {
                          "key": "K",
                          "description": "Limits K",
                          "valueObject": { "Max": 100, "Rate": 0.2, "Active": true }
                        }
                      ]
                    },
                    {
                      "name": "Rules",
                      "values": [
                        {
                          "key": "AL",
                          "valueObject": {
                            "State": "AL",
                            "Method": "Formula"
                          }
                        }
                      ]
                    },
                    {
                      "name": "CityTax",
                      "values": [
                        { "key": "PHL", "valueObject": { "Rate": 0.03 } }
                      ]
                    },
                    {
                      "name": "Empty",
                      "values": []
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static readonly string Lf = Regulation.Replace("\r\n", "\n");

    private static string Sp(int count) => new(' ', count);

    private static string RegulationPath(TestSatellite s) => Path.Combine(s.Root, "Regulation", "Test.json");

    private static void Write(TestSatellite s, string text = null, bool bom = false) =>
        File.WriteAllText(RegulationPath(s), text ?? Lf, new UTF8Encoding(bom));

    private static string Read(TestSatellite s) => File.ReadAllText(RegulationPath(s));

    private static Entry FileEntry(TestSatellite s, string key, string fileName, string json)
    {
        File.WriteAllText(Path.Combine(s.LexDirectory, fileName), json);
        var entry = TestSatellite.Value(key, null);
        return new Entry
        {
            Key = entry.Key,
            ContentFile = fileName,
            SourceUrl = entry.SourceUrl,
            SourceLocation = entry.SourceLocation,
            LegalReference = entry.LegalReference
        };
    }

    private static ApplyResult Apply(TestSatellite s, Manifest manifest, bool dryRun = false) =>
        Applier.Apply(manifest, s.LexPath, dryRun);

    [Fact]
    public void ScalarValue_IsReplacedInPlace()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(TestSatellite.Value("Rates.A", "9")));

        Assert.Null(result.Error);
        var change = Assert.Single(result.Changes);
        Assert.Equal(ApplyChangeKind.Changed, change.Kind);
        Assert.Equal("Rates.A", change.Target);
        Assert.Equal(Lf.Replace("\"value\": \"8.5\"", "\"value\": \"9\""), Read(s));
    }

    [Fact]
    public void EqualValue_IsUnchanged()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(TestSatellite.Value("Rates.A", "8.50")));

        Assert.Empty(result.Changes);
        Assert.Equal(1, result.Unchanged);
        Assert.Empty(result.WrittenFiles);
        Assert.Equal(Lf, Read(s));
    }

    [Fact]
    public void PropertyPath_KeepsTokenType()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(
            TestSatellite.Value("Limits.K.Max", "120"),
            TestSatellite.Value("Limits.K.Active", "false")));

        Assert.Equal(2, result.Changes.Count);
        Assert.Equal("Limits.K/Max", result.Changes[0].Target);
        Assert.Contains("{ \"Max\": 120, \"Rate\": 0.2, \"Active\": false }", Read(s));
    }

    [Fact]
    public void PropertyPath_NumberWithText_IsSkipped()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(TestSatellite.Value("Limits.K.Max", "one hundred")));

        Assert.Empty(result.Changes);
        Assert.Contains(result.Skipped, x => x.Contains("is not a number", StringComparison.Ordinal));
        Assert.Equal(Lf, Read(s));
    }

    [Fact]
    public void UnknownPropertyPath_IsSkipped()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(TestSatellite.Value("Limits.K.Min", "1")));

        Assert.Contains(result.Skipped, x => x.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void ValueObject_IsReplacedByInlineJson()
    {
        using var s = new TestSatellite();
        Write(s);

        Apply(s, TestSatellite.Manifest(TestSatellite.Value("Limits.K", "{\"Max\":100,\"Rate\":0.25,\"Active\":true}")));

        Assert.Contains("\"valueObject\": {\"Max\":100,\"Rate\":0.25,\"Active\":true}", Read(s));
        Assert.Contains("\"description\": \"Limits K\"", Read(s));
    }

    [Fact]
    public void ContentFile_WithEditionSuffix_IsReindented()
    {
        using var s = new TestSatellite();
        Write(s);
        var json = "{\n  \"State\": \"AL\",\n  \"Method\": \"Table\"\n}";

        var result = Apply(s, TestSatellite.Manifest(FileEntry(s, "Rules.AL.2026", "Rules.AL.2026.json", json)));

        Assert.Equal("Rules.AL", Assert.Single(result.Changes).Target);
        Assert.Contains($"\"valueObject\": {{\n{Sp(20)}\"State\": \"AL\",\n{Sp(20)}\"Method\": \"Table\"\n{Sp(18)}}}", Read(s));
    }

    [Fact]
    public void ContentFile_ForSingleValueLookup()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(FileEntry(s, "CityTax.2026", "CityTax.2026.json", "{ \"Rate\": 0.035 }")));

        Assert.Equal("CityTax.PHL", Assert.Single(result.Changes).Target);
        Assert.Contains("{ \"key\": \"PHL\", \"valueObject\": { \"Rate\": 0.035 } }", Read(s));
    }

    [Fact]
    public void MissingValue_IsAdded_AndSecondRunIsUnchanged()
    {
        using var s = new TestSatellite();
        Write(s);
        var manifest = TestSatellite.Manifest(
            FileEntry(s, "Rules.ZZ.2026", "Rules.ZZ.2026.json", "{\n  \"State\": \"ZZ\"\n}"),
            TestSatellite.Value("Rates.C", "12"));

        var first = Apply(s, manifest);

        Assert.Equal(2, first.Changes.Count);
        Assert.All(first.Changes, x => Assert.Equal(ApplyChangeKind.Added, x.Kind));
        var text = Read(s);
        Assert.Contains($"{{ \"key\": \"B\", \"value\": \"10\" }},\n{Sp(16)}{{\n{Sp(18)}\"key\": \"C\",\n{Sp(18)}\"value\": \"12\"\n{Sp(16)}}}", text);
        Assert.Contains($"\"key\": \"ZZ\",\n{Sp(18)}\"valueObject\": {{\n{Sp(20)}\"State\": \"ZZ\"\n{Sp(18)}}}", text);

        var second = Apply(s, manifest);
        Assert.Empty(second.Changes);
        Assert.Equal(2, second.Unchanged);
        Assert.Equal(text, Read(s));
    }

    [Fact]
    public void AddedValues_PassTheCheck()
    {
        using var s = new TestSatellite();
        Write(s);
        var manifest = TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "9"),
            TestSatellite.Value("Rates.C", "12"),
            TestSatellite.Value("Limits.K.Rate", "0.3"));

        Apply(s, manifest);
        var check = Checker.Check(manifest, s.LexPath);

        Assert.Equal(0, check.Drift);
        Assert.Equal(0, check.Orphans);
    }

    [Fact]
    public void NewValue_InEmptyLookup()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(TestSatellite.Value("Empty.X", "1")));

        Assert.Equal(ApplyChangeKind.Added, Assert.Single(result.Changes).Kind);
        Assert.Contains($"\"values\": [\n{Sp(16)}{{\n{Sp(18)}\"key\": \"X\",\n{Sp(18)}\"value\": \"1\"\n{Sp(16)}}}\n{Sp(14)}]", Read(s));
    }

    [Fact]
    public void NewValue_WithOtherForm_IsSkipped()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(
            TestSatellite.Value("Limits.Regime3.Max", "5"),
            TestSatellite.Value("Rates.D", "{\"Rate\":1}")));

        Assert.Empty(result.Changes);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Equal(Lf, Read(s));
    }

    [Fact]
    public void EntryWithoutLookup_IsSkipped()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(
            TestSatellite.Value("Other.X", "1"),
            TestSatellite.Value("OfficialCalculator.Tax", "https://example.org/calc")));

        Assert.Equal("Other.X: no regulation lookup", Assert.Single(result.Skipped));
    }

    [Fact]
    public void DryRun_WritesNothing()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(TestSatellite.Value("Rates.A", "9"), TestSatellite.Value("Rates.C", "1")), dryRun: true);

        Assert.Equal(2, result.Changes.Count);
        Assert.Empty(result.WrittenFiles);
        Assert.Equal(Lf, Read(s));
    }

    [Fact]
    public void ByteOrderMarkAndLineEndings_AreKept()
    {
        using var s = new TestSatellite();
        var crlf = Lf.Replace("\n", "\r\n");
        Write(s, crlf, bom: true);

        Apply(s, TestSatellite.Manifest(TestSatellite.Value("Rates.C", "12")));

        var bytes = File.ReadAllBytes(RegulationPath(s));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.DoesNotMatch("(?<!\r)\n", text);
        Assert.Contains("\"key\": \"C\"", text);
    }

    [Fact]
    public void EmptyContent_IsSkipped()
    {
        using var s = new TestSatellite();
        Write(s);

        var result = Apply(s, TestSatellite.Manifest(TestSatellite.Value("Rates.A", "")));

        Assert.Equal("Rates.A: content is empty", Assert.Single(result.Skipped));
    }

    [Fact]
    public void RegulationWithoutLookups_IsNote()
    {
        using var s = new TestSatellite();
        s.WriteRegulation("[]");

        var result = Apply(s, TestSatellite.Manifest(TestSatellite.Value("Rates.A", "9")));

        Assert.Null(result.Error);
        Assert.NotNull(result.Note);
    }

    [Fact]
    public void Check_ComparesContentFiles()
    {
        using var s = new TestSatellite();
        Write(s);
        var manifest = TestSatellite.Manifest(FileEntry(s, "Rules.AL.2026", "Rules.AL.2026.json", "{ \"State\": \"AL\", \"Method\": \"Table\" }"));

        var result = Checker.Check(manifest, s.LexPath);

        Assert.Equal(1, result.Drift);
        Assert.Contains("/Method", Assert.Single(result.DriftSamples));
    }
}
