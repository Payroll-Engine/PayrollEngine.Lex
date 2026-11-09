using System.Text;
using Xunit;

namespace PayrollEngine.Lex.Tests;

/// <summary>
/// Text table entries applied to the regulation: lookup archives, comparison, check, verification and rollover
/// </summary>
public class TextTableApplyTests
{
    private const string ArchiveName = "tariffs-2026.zip";
    private const string EntryName = "tariff-a.txt";
    private const string MapName = "Tariff.map.json";
    private const string LookupFile = "Lookups.TariffA.json";
    private const string LookupArchive = "Lookups.TariffA.zip";
    private const string ArchiveUrl = "https://example.org/tariffs-2026.zip";

    private const string MainRegulation = """
        {
          "tenants": [
            {
              "regulations": [
                {
                  "name": "XX.Test",
                  "namespace": "XX",
                  "validFrom": "2026-01-01T00:00:00Z",
                  "wageTypes": [],
                  "lookups": [
                    {
                      "name": "XX.Rates",
                      "values": [ { "key": "A", "value": "7.5" } ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private const string Placeholder = """
        {
          "tenants": [
            {
              "identifier": "XX.Test",
              "regulations": [
                {
                  "name": "XX.Test",
                  "validFrom": "2026-01-01T00:00:00Z",
                  "lookups": [
                    {
                      "name": "XX.TariffA",
                      "values": []
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static string RegulationDirectory(TestSatellite s) => Path.Combine(s.Root, "Regulation");

    private static TestSatellite Satellite(string placeholder = Placeholder, string text = null)
    {
        var s = new TestSatellite();
        WriteText(Path.Combine(RegulationDirectory(s), "Main.json"), MainRegulation);
        WriteText(Path.Combine(RegulationDirectory(s), LookupFile), placeholder);
        WriteTable(s, text ?? TariffSample.Text());
        File.WriteAllText(Path.Combine(s.LexDirectory, MapName), TariffSample.MapJson);
        return s;
    }

    /// <summary>Regulation files with CRLF line breaks</summary>
    private static void WriteText(string path, string text) =>
        File.WriteAllText(path, text.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));

    private static void WriteTable(TestSatellite s, string text)
    {
        var path = Path.Combine(s.LexDirectory, ArchiveName);
        File.Delete(path);
        TariffSample.WriteArchive(path, EntryName, text);
    }

    private static Entry TableEntry(string key = "TariffA") => new()
    {
        Key = key,
        ContentFile = ArchiveName + "!" + EntryName,
        ContentMap = MapName,
        SourceUrls = ["https://example.org/doc"],
        SourceLocation = "tariff table A",
        LegalReference = "Test Act s. 3"
    };

    private static Manifest TableManifest(Entry entry = null, bool archiveUrl = true)
    {
        var manifest = TestSatellite.Manifest(TestSatellite.Value("Rates.A", "7.5"), entry ?? TableEntry());
        manifest.Files.Add(new EvidenceFile { FileName = ArchiveName, SourceUrls = archiveUrl ? [ArchiveUrl] : null });
        return manifest;
    }

    private static ApplyResult Apply(TestSatellite s, Manifest manifest = null, bool dryRun = false) =>
        Applier.Apply(manifest ?? TableManifest(), s.LexPath, dryRun);

    private static string LookupEntry(TestSatellite s) =>
        Encoding.UTF8.GetString(ArchiveWriter.ReadEntries(Path.Combine(RegulationDirectory(s), LookupArchive))
            .Single(x => x.Name == LookupFile).Content);

    #region Apply

    [Fact]
    public void Apply_FirstRun_ConvertsLookupFileToArchive()
    {
        using var s = Satellite();

        var result = Apply(s);

        Assert.Null(result.Error);
        var change = Assert.Single(result.Changes);
        Assert.Equal(ApplyChangeKind.Table, change.Kind);
        Assert.Equal("XX.TariffA", change.Target);
        Assert.Equal(LookupArchive, change.File);
        Assert.Equal(TariffSample.FilteredCount, change.AddedValues);
        Assert.Equal($"+{TariffSample.FilteredCount} added, -0 removed, ~0 changed", change.Detail);
        Assert.Equal([$"{LookupFile} -> {LookupArchive}"], result.ReplacedFiles);
        Assert.False(File.Exists(Path.Combine(RegulationDirectory(s), LookupFile)));
        Assert.Contains(Path.Combine(RegulationDirectory(s), LookupArchive), result.WrittenFiles);

        var json = LookupEntry(s);
        var indent = new string(' ', 16);
        Assert.Contains(indent + "{ \"key\": \"A0N\", \"rangeValue\": 1, \"valueObject\": { \"Code\": \"A0N\", \"MinimumTax\": 0, \"TaxRate\": 0 } },\r\n", json);
        Assert.Contains(indent + "{ \"key\": \"B1Y\", \"rangeValue\": 51, \"valueObject\": { \"Code\": \"B1Y\", \"MinimumTax\": 15, \"TaxRate\": 0.031 } },\r\n", json);
        Assert.Contains(indent + "{ \"key\": \"MEN\", \"rangeValue\": 1, \"valueObject\": { \"Code\": \"MEN\", \"MinimumTax\": 0, \"TaxRate\": 0.295 } }\r\n" +
                        new string(' ', 14) + "]", json);
        // records 00, 12, 13 and 99 are filtered by the map
        Assert.DoesNotContain("PEL", json);
        Assert.DoesNotContain("MED", json);
    }

    [Fact]
    public void Apply_SecondRun_ChangesNothing()
    {
        using var s = Satellite();
        Apply(s);
        var archivePath = Path.Combine(RegulationDirectory(s), LookupArchive);
        var bytes = File.ReadAllBytes(archivePath);

        var result = Apply(s);

        Assert.Null(result.Error);
        Assert.Empty(result.Changes);
        Assert.Empty(result.WrittenFiles);
        Assert.Empty(result.ReplacedFiles);
        // the table rows and the rates value
        Assert.Equal(TariffSample.FilteredCount + 1, result.Unchanged);
        Assert.Equal(bytes, File.ReadAllBytes(archivePath));
    }

    [Fact]
    public void Apply_DryRun_WritesNothing()
    {
        using var s = Satellite();

        var result = Apply(s, dryRun: true);

        Assert.Single(result.Changes);
        Assert.Equal([$"{LookupFile} -> {LookupArchive}"], result.ReplacedFiles);
        Assert.Empty(result.WrittenFiles);
        Assert.True(File.Exists(Path.Combine(RegulationDirectory(s), LookupFile)));
        Assert.False(File.Exists(Path.Combine(RegulationDirectory(s), LookupArchive)));
    }

    [Fact]
    public void Apply_ChangedTable_ReplacesArchiveEntry()
    {
        using var s = Satellite();
        Apply(s);
        // one tariff removed, one rate changed
        var tariffs = TariffSample.Tariffs.Take(4).ToList();
        tariffs[1] = tariffs[1] with { Rate = 130 };
        WriteTable(s, TariffSample.Text(tariffs));

        var result = Apply(s);

        Assert.Null(result.Error);
        var change = Assert.Single(result.Changes);
        Assert.Equal("+0 added, -1 removed, ~1 changed", change.Detail);
        Assert.Equal((0, 1, 1), (change.AddedValues, change.RemovedValues, change.ChangedValues));
        Assert.Empty(result.ReplacedFiles);
        Assert.Equal([Path.Combine(RegulationDirectory(s), LookupArchive)], result.WrittenFiles);
        Assert.Contains("\"TaxRate\": 0.013 }", LookupEntry(s));
    }

    [Fact]
    public void Apply_SameTable_GivesSameArchiveBytes()
    {
        using var first = Satellite();
        using var second = Satellite();

        Apply(first);
        Apply(second);

        Assert.Equal(File.ReadAllBytes(Path.Combine(RegulationDirectory(first), LookupArchive)),
            File.ReadAllBytes(Path.Combine(RegulationDirectory(second), LookupArchive)));
    }

    [Fact]
    public void Apply_EditionTag_AddressesLookup()
    {
        using var s = Satellite();
        var result = Apply(s, TableManifest(TableEntry("TariffA.2026")));
        Assert.Equal(ApplyChangeKind.Table, Assert.Single(result.Changes).Kind);
    }

    [Fact]
    public void Apply_LookupWithOtherObjects_StaysJson()
    {
        using var s = Satellite(Placeholder.Replace("\"lookups\": [", "\"wageTypes\": [],\n          \"lookups\": ["));

        var result = Apply(s);

        Assert.Null(result.Error);
        Assert.Equal(LookupFile, Assert.Single(result.Changes).File);
        Assert.Empty(result.ReplacedFiles);
        Assert.False(File.Exists(Path.Combine(RegulationDirectory(s), LookupArchive)));
        Assert.Contains("\"key\": \"A0N\"", File.ReadAllText(Path.Combine(RegulationDirectory(s), LookupFile)));
    }

    [Fact]
    public void Apply_JsonAndArchiveExist_IsError()
    {
        using var s = Satellite();
        File.WriteAllBytes(Path.Combine(RegulationDirectory(s), LookupArchive),
            ArchiveWriter.Write([("readme.txt", "x"u8.ToArray())]));

        var result = Apply(s);

        Assert.Contains("both exist", result.Error);
        Assert.Empty(result.WrittenFiles);
        Assert.True(File.Exists(Path.Combine(RegulationDirectory(s), LookupFile)));
    }

    [Fact]
    public void Apply_DuplicateRows_IsSkipped()
    {
        using var s = Satellite(text: TariffSample.Text([.. TariffSample.Tariffs, TariffSample.Tariffs[0]]));

        var result = Apply(s);

        Assert.Empty(result.Changes);
        Assert.Contains(result.Skipped, x => x.StartsWith("TariffA: duplicate table row A0N 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_InvalidMap_IsSkipped()
    {
        using var s = Satellite();
        File.WriteAllText(Path.Combine(s.LexDirectory, MapName), "{ \"values\": [] }");

        var result = Apply(s);

        Assert.Empty(result.Changes);
        Assert.Contains(result.Skipped, x => x.Contains("is invalid"));
    }

    [Fact]
    public void Apply_UnknownLookup_IsSkipped()
    {
        using var s = Satellite();
        var result = Apply(s, TableManifest(TableEntry("TariffB")));
        Assert.Contains("TariffB: no regulation lookup", result.Skipped);
    }

    #endregion

    #region Archive

    [Fact]
    public void ArchiveWriter_IsDeterministic()
    {
        var entries = new List<(string, byte[])> { ("b.json", "{}"u8.ToArray()), ("a.json", "[]"u8.ToArray()) };

        var first = ArchiveWriter.Write(entries);
        var second = ArchiveWriter.Write(entries.AsEnumerable().Reverse());

        Assert.Equal(first, second);
    }

    [Fact]
    public void ArchiveWriter_Replace_KeepsOtherEntries()
    {
        // ReSharper disable once StringLiteralTypo
        var path = Path.Combine(Path.GetTempPath(), "lexzip-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            File.WriteAllBytes(path, ArchiveWriter.Write([("a.json", "{}"u8.ToArray()), ("b.txt", "b"u8.ToArray())]));

            var bytes = ArchiveWriter.Replace(path, new Dictionary<string, byte[]> { ["a.json"] = "[]"u8.ToArray() });
            File.WriteAllBytes(path, bytes);

            var entries = ArchiveWriter.ReadEntries(path);
            Assert.Equal(["a.json", "b.txt"], entries.Select(x => x.Name));
            Assert.Equal("[]", Encoding.UTF8.GetString(entries[0].Content));
            Assert.Equal("b", Encoding.UTF8.GetString(entries[1].Content));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RegulationReader_ReadsArchiveEntries()
    {
        using var s = Satellite();
        Apply(s);

        var regulation = RegulationReader.Read(s.Root);

        var lookup = regulation.Lookups.Single(x => x.Name == "XX.TariffA");
        Assert.Equal(LookupFile, lookup.File.EntryName);
        Assert.Equal(TariffSample.FilteredCount, lookup.Values.Count);
        Assert.Equal("TariffA", lookup.Alias);
        Assert.True(lookup.File.LookupsOnly);
        Assert.False(regulation.Lookups.Single(x => x.Name == "XX.Rates").File.LookupsOnly);
    }

    #endregion

    #region Check and verification

    [Fact]
    public void Check_AppliedTable_IsCoveredWithoutDrift()
    {
        using var s = Satellite();
        Apply(s);

        var result = Checker.Check(TableManifest(), s.LexPath);

        Assert.Null(result.Error);
        Assert.Equal(0, result.Drift);
        Assert.Equal(0, result.Uncovered);
        Assert.Equal(TariffSample.FilteredCount, result.CoveredByLookup);
    }

    [Fact]
    public void Check_ChangedTable_IsDrift()
    {
        using var s = Satellite();
        Apply(s);
        WriteTable(s, TariffSample.Text(TariffSample.Tariffs.Take(4)));

        var result = Checker.Check(TableManifest(), s.LexPath);

        Assert.Equal(1, result.Drift);
        Assert.Contains(result.DriftSamples, x => x == "TariffA: table XX.TariffA +0 added, -1 removed, ~0 changed");
    }

    [Fact]
    public async Task VerifyManifest_DeclaredArchive_IsFileIdentity()
    {
        using var s = Satellite();

        var results = await ManifestVerifier.VerifyManifest(TableManifest(), s.LexDirectory);

        var result = results.Single(x => x.Key == "TariffA");
        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.True(result.FileIdentity);
        Assert.Equal(ArchiveName, result.Evidence);
        Assert.True(result.CanWrite);
        var verification = ManifestVerifier.FileIdentityVerification(new DateOnly(2026, 11, 9), result.Evidence);
        Assert.Equal(SourceVerifier.Method + "; file identity", verification.Method);
        Assert.Equal(ArchiveName, verification.Evidence);
    }

    [Fact]
    public async Task VerifyManifest_ArchiveWithoutUrl_HasNoEvidence()
    {
        using var s = Satellite();

        var results = await ManifestVerifier.VerifyManifest(TableManifest(archiveUrl: false), s.LexDirectory);

        var result = results.Single(x => x.Key == "TariffA");
        Assert.Equal(SourceMatch.NoEvidence, result.Match);
        Assert.False(result.FileIdentity);
        // the note tells why, the generic no-evidence summary does not
        Assert.Contains($"text table {ArchiveName} is not declared in files[]", result.Note);
    }

    [Fact]
    public async Task VerifyManifest_Live_DoesNotLoadTheArchive()
    {
        using var s = Satellite();
        var loaded = new List<string>();

        var results = await ManifestVerifier.VerifyManifest(TableManifest(), s.LexDirectory,
            url => { loaded.Add(url); return Task.FromResult(SourceDocument.FromText("rate 7.5")); });

        Assert.True(results.Single(x => x.Key == "TariffA").FileIdentity);
        Assert.DoesNotContain(ArchiveUrl, loaded);
    }

    #endregion

    #region Rollover

    [Theory]
    [InlineData("tariffs-2026.zip!tariff26a.txt", "tariffs-2027.zip!tariff27a.txt")]
    [InlineData("rates2026set.zip!rates26xy.txt", "rates2027set.zip!rates27xy.txt")]
    [InlineData("t202612.zip!a126.txt", "t202612.zip!a126.txt")]
    public void RenameArchiveReference_ReplacesYearAndShortYear(string reference, string expected) =>
        Assert.Equal(expected, Rollover.RenameArchiveReference(reference, "2026", "2027"));

    [Fact]
    public void Rollover_ArchiveLookupAndReference_AreRolled()
    {
        using var s = new RolloverSatellite();
        var lookup = Placeholder.Replace("\r\n", "\n");
        File.WriteAllBytes(Path.Combine(s.Root, "Regulation", LookupArchive),
            ArchiveWriter.Write([(LookupFile, Encoding.UTF8.GetBytes(lookup))]));
        s.Write(".lex/lex.json", RolloverSatellite.LexJson.Replace("\"values\": [", """
            "values": [
                {
                  "key": "TariffA",
                  "contentFile": "tariffs-2026.zip!tariff26a.txt",
                  "contentMap": "Tariff.map.json",
                  "sourceUrls": ["https://example.org/doc"],
                  "sourceLocation": "tariff table A",
                  "legalReference": "Test Act s. 3"
                },
            """));
        File.WriteAllBytes(Path.Combine(s.Root, ".lex", ArchiveName), ArchiveWriter.Write([("tariff26a.txt", "x"u8.ToArray())]));

        var result = Rollover.Run(s.Root, new DateTime(2027, 1, 1), dryRun: false);

        Assert.Null(result.Error);
        var archive = ArchiveWriter.ReadEntries(Path.Combine(s.Target, "Regulation", LookupArchive));
        Assert.Contains("\"validFrom\": \"2027-01-01T00:00:00Z\"", Encoding.UTF8.GetString(archive.Single().Content));
        Assert.Contains($"Regulation{Path.DirectorySeparatorChar}{LookupArchive}: validFrom", result.Changed);
        // the evidence archive keeps its name, the reference points to the archive of the new cycle
        Assert.True(File.Exists(Path.Combine(s.Target, ".lex", ArchiveName)));
        Assert.Equal("tariffs-2027.zip!tariff27a.txt", s.TargetManifest().Values.Single(x => x.Key == "TariffA").ContentFile);
        Assert.Contains(result.Notes, x => x.Contains("tariffs-2026.zip!tariff26a.txt -> tariffs-2027.zip!tariff27a.txt"));
    }

    #endregion
}
