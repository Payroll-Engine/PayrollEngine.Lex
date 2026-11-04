using Xunit;

namespace PayrollEngine.Lex.Tests;

public sealed class LexFileResolverTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lexroot-" + Guid.NewGuid().ToString("N"));

    public LexFileResolverTests()
    {
        WriteLex("Payroll.A", "Data.Tax.2026");
        WriteLex("Payroll.A", "Data.Tax.2025");
        WriteLex("Payroll.B", "Data.Rates.2026");
        WriteLex("Payroll.B", Path.Combine("Tests", "bin", "Debug", "Data.Rates.2026"));
        WriteLex("Other.C", "Data.Tax.2026");
    }

    private void WriteLex(string repository, string satellite)
    {
        var directory = Path.Combine(root, repository, satellite, ".lex");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "lex.json"), "{}");
    }

    private List<string> Relative(List<string> files) =>
        files.Select(x => Path.GetRelativePath(root, x).Replace('\\', '/')).ToList();

    [Fact]
    public void Root_ResolvesAllRepositories()
    {
        var files = Relative(LexFileResolver.Resolve(root, null, "lex.json", true));

        Assert.Equal(
        [
            "Other.C/Data.Tax.2026/.lex/lex.json",
            "Payroll.A/Data.Tax.2025/.lex/lex.json",
            "Payroll.A/Data.Tax.2026/.lex/lex.json",
            "Payroll.B/Data.Rates.2026/.lex/lex.json"
        ], files);
    }

    [Fact]
    public void Root_RepoMaskFiltersRepositories()
    {
        var files = Relative(LexFileResolver.Resolve(root, "Payroll.*", "lex.json", true));

        Assert.Equal(3, files.Count);
        Assert.DoesNotContain(files, x => x.StartsWith("Other.C/", StringComparison.Ordinal));
    }

    [Fact]
    public void Root_IgnoresBuildOutput() =>
        Assert.DoesNotContain(Relative(LexFileResolver.Resolve(root, "Payroll.B", "lex.json", true)),
            x => x.Contains("/bin/", StringComparison.Ordinal));

    [Fact]
    public void Root_MissingFolder_ResolvesNothing() =>
        Assert.Empty(LexFileResolver.Resolve(Path.Combine(root, "missing"), null, "lex.json", true));

    [Fact]
    public void Root_WithoutRoot_UsesFileMask()
    {
        var lexPath = Path.Combine(root, "Payroll.A", "Data.Tax.2026", ".lex", "lex.json");

        Assert.Equal([Path.GetFullPath(lexPath)], LexFileResolver.Resolve(null, null, lexPath, false));
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); }
        catch (IOException) { /* best effort */ }
    }
}
