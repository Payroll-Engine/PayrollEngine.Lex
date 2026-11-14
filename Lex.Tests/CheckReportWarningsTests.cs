using Xunit;

namespace PayrollEngine.Lex.Tests;

public class CheckReportWarningsTests
{
    private static CheckResult Result(string satellite, params string[] warnings)
    {
        var result = new CheckResult { Satellite = satellite, LexPath = $"C:/Repos/XX/{satellite}/.lex/lex.json" };
        result.Warnings.AddRange(warnings);
        return result;
    }

    [Fact]
    public void Markdown_WithoutWarnings_HasNoWarningsSection()
    {
        var markdown = CheckReport.BuildMarkdown([Result("Data.Tax.2026")]);

        Assert.DoesNotContain("## Warnings", markdown);
    }

    [Fact]
    public void Markdown_Warning_IsListedOnceWithTheSatellitesItConcerns()
    {
        const string warning = "git not available: evidence versioning not checked";

        var markdown = CheckReport.BuildMarkdown(
            [Result("Data.Tax.2026", warning), Result("Data.Rates.2026", warning), Result("Data.Other.2026")]);

        Assert.Contains("## Warnings", markdown);
        Assert.Contains($"- {warning} (Data.Tax.2026, Data.Rates.2026)", markdown);
        Assert.Single(markdown.Split('\n'), x => x.Contains(warning));
    }

    [Fact]
    public void Warning_IsNoFinding()
    {
        var without = Result("Data.Tax.2026");
        var with = Result("Data.Tax.2026", "git not available: evidence versioning not checked");

        Assert.Equal(without.HasFindings, with.HasFindings);
    }
}
