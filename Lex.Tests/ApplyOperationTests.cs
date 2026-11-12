using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ApplyOperationTests
{
    private const string Lookups = """
        [ { "name": "Rates", "values": [ { "key": "A", "value": "8.5" }, { "key": "B", "value": "10" } ] } ]
        """;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static TestSatellite Satellite(params Entry[] values)
    {
        var s = new TestSatellite();
        s.WriteRegulation(Lookups);
        File.WriteAllText(s.LexPath, JsonSerializer.Serialize(TestSatellite.Manifest(values), WriteOptions));
        return s;
    }

    private static string Regulation(TestSatellite s) => File.ReadAllText(Path.Combine(s.Root, "Regulation", "Test.json"));

    [Fact]
    public async Task Manifest_IsApplied()
    {
        using var s = Satellite(TestSatellite.Value("Rates.A", "9"), TestSatellite.Value("Rates.C", "12"));

        var result = await ApplyOperation.ApplyFileAsync(s.LexPath, dryRun: false);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Changes.Count);
        Assert.Contains("\"value\": \"9\"", Regulation(s));
    }

    [Fact]
    public async Task DryRun_WritesNothing()
    {
        using var s = Satellite(TestSatellite.Value("Rates.A", "9"));
        var before = Regulation(s);

        var result = await ApplyOperation.ApplyFileAsync(s.LexPath, dryRun: true);

        Assert.Single(result.Changes);
        Assert.Empty(result.WrittenFiles);
        Assert.Equal(before, Regulation(s));
    }

    [Fact]
    public async Task Comments_AreAccepted()
    {
        using var s = Satellite(TestSatellite.Value("Rates.A", "9"));
        await File.WriteAllTextAsync(s.LexPath, "// comment\n" + await File.ReadAllTextAsync(s.LexPath));

        var result = await ApplyOperation.ApplyFileAsync(s.LexPath, dryRun: true);

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task UnreadableManifest_IsAnError()
    {
        using var s = Satellite();
        await File.WriteAllTextAsync(s.LexPath, "{ \"values\": [ ");

        var result = await ApplyOperation.ApplyFileAsync(s.LexPath, dryRun: false);

        Assert.Equal(s.LexPath, result.LexPath);
        Assert.StartsWith("lex.json could not be read: ", result.Error);
    }

    [Fact]
    public async Task EmptyManifest_IsAnError()
    {
        using var s = Satellite();
        await File.WriteAllTextAsync(s.LexPath, "null");

        var result = await ApplyOperation.ApplyFileAsync(s.LexPath, dryRun: false);

        Assert.Equal("lex.json is empty", result.Error);
    }

    [Fact]
    public async Task InvalidManifest_ReportsTheFirstError()
    {
        using var s = Satellite();
        await File.WriteAllTextAsync(s.LexPath, "{ \"values\": [] }");
        var before = Regulation(s);

        var result = await ApplyOperation.ApplyFileAsync(s.LexPath, dryRun: false);

        Assert.Equal("lex.json is invalid: Missing required field: authority", result.Error);
        Assert.Equal(before, Regulation(s));
    }

    [Fact]
    public void Summary_CountsChangesAndFailures()
    {
        var summary = new ApplySummary();
        var applied = new ApplyResult { Unchanged = 3 };
        applied.Changes.Add(new ApplyChange { Kind = ApplyChangeKind.Changed });
        applied.Changes.Add(new ApplyChange { Kind = ApplyChangeKind.Added });
        applied.Changes.Add(new ApplyChange { Kind = ApplyChangeKind.Added });
        applied.Changes.Add(new ApplyChange { Kind = ApplyChangeKind.Table });
        applied.Skipped.Add("Other.X: no regulation lookup");
        var failed = new ApplyResult { Error = "lex.json is empty", Unchanged = 5 };
        failed.Skipped.Add("ignored");

        summary.Add(applied);
        summary.Add(failed);

        Assert.Equal(1, summary.Changed);
        Assert.Equal(2, summary.Added);
        Assert.Equal(1, summary.Tables);
        Assert.Equal(4, summary.Applied);
        Assert.Equal(3, summary.Unchanged);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(1, summary.Failed);
    }

    [Fact]
    public void ReplacedFile_HasReferenceHint()
    {
        var result = new ApplyResult();
        result.ReplacedFiles.Add("Lookups.Tariff.json -> Lookups.Tariff.zip");

        Assert.Equal(["Lookups.Tariff.json -> Lookups.Tariff.zip: update installFiles (regulation-package.json), the project file and Setup.pecmd"],
            ApplyOperation.ReplacedFileHints(result));
    }
}
