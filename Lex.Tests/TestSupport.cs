namespace PayrollEngine.Lex.Tests;

/// <summary>
/// Temporary data satellite: `Regulation` folder with one lookup file and a `.lex` folder
/// </summary>
internal sealed class TestSatellite : IDisposable
{
    // ReSharper disable once StringLiteralTypo
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "lextest-" + Guid.NewGuid().ToString("N"), "Data.Test.2026");
    internal string LexDirectory => Path.Combine(Root, ".lex");
    internal string LexPath => Path.Combine(LexDirectory, "lex.json");

    internal TestSatellite()
    {
        Directory.CreateDirectory(Path.Combine(Root, "Regulation"));
        Directory.CreateDirectory(LexDirectory);
    }

    internal void WriteRegulation(string lookupsJson, string validFrom = "2026-01-01") =>
        File.WriteAllText(Path.Combine(Root, "Regulation", "Test.json"),
            "{ \"tenants\": [ { \"regulations\": [ { \"name\": \"Test.Regulation\", \"validFrom\": \"" + validFrom +
            "T00:00:00Z\", \"lookups\": " + lookupsJson + " } ] } ] }");

    internal static Entry Value(string key, string content, string location = "p. 1") => new()
    {
        Key = key,
        Content = content,
        SourceUrl = "https://example.org/doc",
        SourceLocation = location,
        LegalReference = "Test Act s. 1"
    };

    internal static Manifest Manifest(params Entry[] values) => new()
    {
        Authority = "XX",
        Regulation = "Data.Test",
        ValidFrom = "2026-01-01",
        Provider = "Test Authority",
        Sources = [new Source { Url = "https://example.org/doc", Description = "Test source" }],
        Values = [.. values]
    };

    internal async Task<Snapshot> WriteSnapshot(Manifest manifest)
    {
        var snapshot = await new SnapshotWriter().WriteAsync(manifest, LexDirectory, LexDirectory, false);
        return snapshot;
    }

    public void Dispose()
    {
        try { Directory.Delete(Directory.GetParent(Root)!.FullName, true); }
        catch (IOException) { /* best effort */ }
    }
}
