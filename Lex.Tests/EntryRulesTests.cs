using Xunit;

namespace PayrollEngine.Lex.Tests;

public class EntryRulesTests
{
    [Theory]
    [InlineData("Rates.Standard", true)]
    [InlineData("Rates.2026.Standard", true)]
    [InlineData("Rates", false)]
    [InlineData(".Standard", false)]
    [InlineData("Rates.", false)]
    [InlineData("Rates.Standard rate", false)]
    [InlineData("Rates.*", false)]
    [InlineData("", false)]
    public void KeyError(string key, bool valid) =>
        Assert.Equal(valid, EntryRules.KeyError(key) == null);

    [Theory]
    [InlineData(null, true)]
    [InlineData("8.5", true)]
    [InlineData("{ \"rate\": 8.5 }", true)]
    [InlineData("[1, 2]", true)]
    [InlineData("{ \"rate\": 8.5 ", false)]
    public void ContentError(string content, bool valid) =>
        Assert.Equal(valid, EntryRules.ContentError(content) == null);

    [Theory]
    [InlineData("2026-11-03", true)]
    [InlineData("2026-13-01", false)]
    [InlineData("03.11.2026", false)]
    public void IsDate(string date, bool valid) =>
        Assert.Equal(valid, EntryRules.IsDate(date));

    [Theory]
    [InlineData("Rates", "Rates.A", true)]
    [InlineData("Rates", "Rates", true)]
    [InlineData("Rates", "RatesX.A", false)]
    [InlineData("Ra*", "RatesX.A", true)]
    [InlineData("Rates.A", "Rates.AB", false)]
    public void KeyPatternMatches(string pattern, string key, bool match) =>
        Assert.Equal(match, KeyPattern.Matches(pattern, key));

    [Fact]
    public void ResolveManifest_FindsLexFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "lexresolve-" + Guid.NewGuid().ToString("N"));
        var lexDirectory = Path.Combine(root, ".lex");
        Directory.CreateDirectory(lexDirectory);
        var lexPath = Path.Combine(lexDirectory, "lex.json");
        File.WriteAllText(lexPath, "{}");
        try
        {
            Assert.Equal(Path.GetFullPath(lexPath), FileResolver.ResolveManifest(root));
            Assert.Equal(Path.GetFullPath(lexPath), FileResolver.ResolveManifest(lexDirectory));
            Assert.Equal(Path.GetFullPath(lexPath), FileResolver.ResolveManifest(lexPath));
            Assert.Null(FileResolver.ResolveManifest(Path.Combine(root, "missing")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
