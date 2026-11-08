using System.Text;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class RolloverTests
{
    private static readonly DateTime CycleStart = new(2027, 1, 1);

    private static string Lf(string text) => text.Replace("\r\n", "\n");

    [Fact]
    public void Run_CreatesTheSatelliteOfTheNextCycle()
    {
        using var s = new RolloverSatellite();

        var result = Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Null(result.Error);
        Assert.Equal(s.Target, result.TargetDirectory);
        Assert.Equal("2026-01-01", result.PreviousValidFrom);
        Assert.Equal("2027-01-01", result.ValidFrom);
        Assert.True(File.Exists(Path.Combine(s.Target, "Regulation", "XX.Test.Data.Test.2027.json")));
        Assert.True(File.Exists(Path.Combine(s.Target, "Regulation.XX.Test.Data.Test.2027.csproj")));
        Assert.True(File.Exists(Path.Combine(s.Target, ".lex", "Table.A.2027.json")));
        Assert.Contains("Data.Test.2026 -> Data.Test.2027", result.Renamed);
    }

    [Fact]
    public void Run_ExcludesBuildOutputAndSnapshot()
    {
        using var s = new RolloverSatellite();

        Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.False(Directory.Exists(Path.Combine(s.Target, "bin")));
        Assert.False(Directory.Exists(Path.Combine(s.Target, "obj")));
        Assert.False(File.Exists(Path.Combine(s.Target, ".lex", "snapshot.json")));
    }

    [Fact]
    public void Run_CopiesEvidenceUnchanged()
    {
        using var s = new RolloverSatellite();

        Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Equal(RolloverSatellite.Evidence, File.ReadAllBytes(Path.Combine(s.Target, ".lex", "evidence-2026.pdf")));
    }

    [Fact]
    public void Run_SetsValidFromOfTheRegulationOnly()
    {
        using var s = new RolloverSatellite();

        Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Equal(Lf(RolloverSatellite.RegulationJson).Replace("\"2026-01-01T00:00:00Z\"", "\"2027-01-01T00:00:00Z\""),
            s.ReadTarget("Regulation/XX.Test.Data.Test.2027.json"));
    }

    [Fact]
    public void Run_KeepsBomAndLineEndings()
    {
        using var s = new RolloverSatellite();
        s.Write("Regulation/XX.Test.Data.Test.2026.json", RolloverSatellite.RegulationJson, bom: true, crlf: true);

        Rollover.Run(s.Root, CycleStart, dryRun: false);

        var bytes = File.ReadAllBytes(Path.Combine(s.Target, "Regulation", "XX.Test.Data.Test.2027.json"));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.Contains("\"validFrom\": \"2027-01-01T00:00:00Z\",\r\n", text);
        Assert.DoesNotMatch("(?<!\r)\n", text);
    }

    [Fact]
    public void Run_SetsTheYearBasedVersion()
    {
        using var s = new RolloverSatellite();

        Rollover.Run(s.Root, CycleStart, dryRun: false);

        var props = s.ReadTarget("Directory.Build.props");
        Assert.Contains("<Version>2027.1-beta.dev</Version>", props);
        Assert.Contains("<FileVersion>2027.1.0</FileVersion>", props);
        Assert.Contains("<Product>Test Data 2026</Product>", props);
        Assert.Contains("\"version\": \"2027.1-beta.dev\"", s.ReadTarget("regulation-package.json"));
    }

    [Fact]
    public void Run_KeepsAVersionWithoutYear()
    {
        using var s = new RolloverSatellite();
        s.Write("regulation-package.json", RolloverSatellite.Package.Replace("2026.1-beta.dev", "1.0.0-beta"));

        Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Contains("\"version\": \"1.0.0-beta\"", s.ReadTarget("regulation-package.json"));
    }

    [Fact]
    public void Run_UpdatesReferencesToRenamedFiles()
    {
        using var s = new RolloverSatellite();

        Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Contains("\"Regulation/XX.Test.Data.Test.2027.json\"", s.ReadTarget("regulation-package.json"));
        Assert.Equal("PayrollImport Regulation/XX.Test.Data.Test.2027.json\n", s.ReadTarget("Setup.pecmd"));
    }

    [Fact]
    public void Run_OpensAllLexEntries()
    {
        using var s = new RolloverSatellite();

        var result = Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Equal(2, result.OpenEntries);
        var manifest = s.TargetManifest();
        Assert.Equal("2027-01-01", manifest.ValidFrom);
        Assert.All(manifest.Values, x => Assert.True(x.Open));
        Assert.All(manifest.Values, x => Assert.Null(x.Verification));
        Assert.Equal("8.5", manifest.Values[0].Content);
        Assert.Equal("Table.A.2027", manifest.Values[1].Key);
        Assert.Equal("Table.A.2027.json", manifest.Values[1].ContentFile);
        Assert.Equal("evidence-2026.pdf", Assert.Single(manifest.Files).FileName);
    }

    [Fact]
    public void Run_ReportsRemainingYearMentions()
    {
        using var s = new RolloverSatellite();

        var result = Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Contains(result.YearMentions, x => x.File == "Directory.Build.props" && x.Text.Contains("Test Data 2026"));
        Assert.Contains(result.YearMentions, x => x.File.EndsWith("XX.Test.Data.Test.2027.json") && x.Text.Contains("Test rates 2026"));
        Assert.DoesNotContain(result.YearMentions, x => x.File == "Setup.pecmd");
    }

    [Fact]
    public void Run_LeavesTheSourceUnchanged()
    {
        using var s = new RolloverSatellite();
        var lex = File.ReadAllText(Path.Combine(s.Root, ".lex", "lex.json"));

        Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Equal(lex, File.ReadAllText(Path.Combine(s.Root, ".lex", "lex.json")));
        Assert.True(File.Exists(Path.Combine(s.Root, ".lex", "snapshot.json")));
    }

    [Fact]
    public void DryRun_WritesNothing()
    {
        using var s = new RolloverSatellite();

        var result = Rollover.Run(s.Root, CycleStart, dryRun: true);

        Assert.Null(result.Error);
        Assert.Equal(2, result.OpenEntries);
        Assert.NotEmpty(result.Changed);
        Assert.False(Directory.Exists(s.Target));
    }

    [Fact]
    public void ExistingTarget_IsAnError()
    {
        using var s = new RolloverSatellite();
        Directory.CreateDirectory(s.Target);

        var result = Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Contains("exists", result.Error);
        Assert.Empty(Directory.GetFileSystemEntries(s.Target));
    }

    [Fact]
    public void CycleOfTheSameYear_IsAnError()
    {
        using var s = new RolloverSatellite();

        var result = Rollover.Run(s.Root, new DateTime(2026, 7, 1), dryRun: false);

        Assert.Contains("does not follow", result.Error);
        Assert.False(Directory.Exists(s.Target));
    }

    [Fact]
    public void FolderWithoutYearTag_IsAnError()
    {
        using var s = new RolloverSatellite("Data.Test");

        var result = Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Contains("no year tag", result.Error);
    }

    [Fact]
    public void MidYearEdition_RollsToTheYearWithoutSuffix()
    {
        using var s = new RolloverSatellite("Data.Test.2026b");
        File.Move(Path.Combine(s.Root, "Regulation", "XX.Test.Data.Test.2026.json"),
            Path.Combine(s.Root, "Regulation", "XX.Test.Data.Test.2026b.json"));
        s.Write("Setup.pecmd", "PayrollImport Regulation/XX.Test.Data.Test.2026b.json\n");
        s.Write("regulation-package.json", RolloverSatellite.Package.Replace("Test.2026.json", "Test.2026b.json"));

        var result = Rollover.Run(s.Root, CycleStart, dryRun: false);

        Assert.Null(result.Error);
        Assert.Equal(s.Target, result.TargetDirectory);
        Assert.Contains("Data.Test.2026b -> Data.Test.2027", result.Renamed);
        Assert.True(File.Exists(Path.Combine(s.Target, "Regulation", "XX.Test.Data.Test.2027.json")));
        Assert.Equal("PayrollImport Regulation/XX.Test.Data.Test.2027.json\n", s.ReadTarget("Setup.pecmd"));
        Assert.Contains("\"Regulation/XX.Test.Data.Test.2027.json\"", s.ReadTarget("regulation-package.json"));
        Assert.Equal("Table.A.2027", s.TargetManifest().Values[1].Key);
    }

    [Theory]
    [InlineData("Data.Tax.2026", "")]
    [InlineData("Data.Tax.2026b", "b")]
    [InlineData("Data.Tax.2026B", null)]
    [InlineData("Data.Tax.2026bc", null)]
    [InlineData("Data.Tax", null)]
    public void EditionSuffix_OfTheFolderName(string folderName, string expected) =>
        Assert.Equal(expected, Rollover.EditionSuffix(folderName, "2026"));

    [Theory]
    [InlineData("XX.Data.Tax.2026b.json", "XX.Data.Tax.2027.json")]
    [InlineData("XX.Data.Tax.2026.json", "XX.Data.Tax.2027.json")]
    [InlineData("XX.Data.Tax.2026c.json", "XX.Data.Tax.2026c.json")]
    public void RenameSegment_DropsTheEditionSuffix(string name, string expected) =>
        Assert.Equal(expected, Rollover.RenameSegment(name, "2026", "2027", "b"));

    [Fact]
    public void MissingLex_IsAnError()
    {
        using var s = new RolloverSatellite();
        File.Delete(Path.Combine(s.Root, ".lex", "lex.json"));

        Assert.Contains("lex.json", Rollover.Run(s.Root, CycleStart, dryRun: false).Error);
    }

    [Fact]
    public void RolledSatellite_CheckReportsOpenEntries()
    {
        using var s = new RolloverSatellite();
        Rollover.Run(s.Root, CycleStart, dryRun: false);

        var lexPath = Path.Combine(s.Target, ".lex", "lex.json");
        var result = Checker.Check(s.TargetManifest(), lexPath);

        Assert.Equal(2, result.Open);
        Assert.Equal(0, result.Unverified);
        Assert.True(result.ValidFromMatch);
        Assert.Equal(0, result.Drift);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void RolledSatellite_ApplySkipsOpenEntries()
    {
        using var s = new RolloverSatellite();
        Rollover.Run(s.Root, CycleStart, dryRun: false);

        var result = Applier.Apply(s.TargetManifest(), Path.Combine(s.Target, ".lex", "lex.json"), dryRun: true);

        Assert.Empty(result.Changes);
        Assert.Equal(2, result.Skipped.Count);
        Assert.All(result.Skipped, x => Assert.EndsWith("open entry, verify first", x));
    }

    [Theory]
    [InlineData("Data.Tax.2026", "Data.Tax.2027")]
    [InlineData("XX.Data.Tax.2026.json", "XX.Data.Tax.2027.json")]
    [InlineData("2026.json", "2027.json")]
    [InlineData("tax-rates-2026.pdf", "tax-rates-2026.pdf")]
    [InlineData("Data.Tax.20261.json", "Data.Tax.20261.json")]
    public void RenameSegment_ReplacesDotSeparatedYear(string name, string expected) =>
        Assert.Equal(expected, Rollover.RenameSegment(name, "2026", "2027"));
}
