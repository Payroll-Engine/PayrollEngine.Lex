using Xunit;

namespace PayrollEngine.Lex.Tests;

public class SourceQuoteTests
{
    private static Entry Quoted(string content, string quote, string unit = null) => new()
    {
        Key = "Parameters.A",
        Content = content,
        Unit = unit,
        SourceQuote = quote,
        SourceUrls = ["https://example.org/guide"]
    };

    private static SourceDocument Pages(params string[] texts) =>
        new() { State = SourceState.Readable, Sections = texts.Select((x, i) => ($"Page {i + 1}", x)).ToList() };

    [Fact]
    public void QuoteOverHyphenatedLineBreak_IsFound()
    {
        var document = Pages(
            "Introduction 2026",
            // ReSharper disable once StringLiteralTypo
            "If the addi-\ntional payments to the employee\nduring the year exceed $1 million, withhold at 39%.");

        var result = SourceVerifier.Verify(
            Quoted("1000000", "If the additional payments to the employee during the year exceed $1 million"), document);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Section \"Page 2\"", result.Location);
        Assert.True(result.CanWrite);
    }

    [Fact]
    public void QuoteWithCopiedHyphenation_IsFound() =>
        Assert.Equal(SourceMatch.Found, SourceVerifier.Verify(
            // ReSharper disable once StringLiteralTypo
            Quoted("1000000", "the addi- tional payments exceed $1 million"),
            Pages("If the additional payments exceed $1 million, withhold at 39%.")).Match);

    [Fact]
    public void QuoteWithTypographicQuotes_IsFound() =>
        Assert.Equal(SourceMatch.Found, SourceVerifier.Verify(
            Quoted("0.5", "at least one-half the employee's \"base rate\"", "factor"),
            Pages("paid at least one-half the employee\u2019s \u201cbase rate\u201d of pay")).Match);

    [Fact]
    public void FactorInWordsWithinQuote_IsFound()
    {
        var result = SourceVerifier.Verify(
            Quoted("1.5", "at one and one-half times the base rate", "factor"),
            Pages("Extra hours above the agreed hours are paid at one and one-half times the base rate."));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Section \"Page 1\"", result.Location);
    }

    [Fact]
    public void QuoteFound_NumberInOtherSection_IsNotFound()
    {
        var result = SourceVerifier.Verify(
            Quoted("8.15", "employees shall be paid the hourly rate"),
            Pages("employees shall be paid the hourly rate stated in the table", "Table: hourly rate 8.15"));

        Assert.Equal(SourceMatch.NotFound, result.Match);
        Assert.Equal(["8.15"], result.Missing);
        Assert.Null(result.Location);
    }

    [Fact]
    public void QuoteMissing_IsQuoteNotFound()
    {
        var result = SourceVerifier.Verify(
            Quoted("8.15", "the hourly rate is eight dollars"),
            Pages("The hourly rate is 8.15 per hour."));

        Assert.Equal(SourceMatch.QuoteNotFound, result.Match);
        Assert.Equal("the hourly rate is eight dollars", result.Quote);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public void QuoteInSecondSource_IsFound()
    {
        var entry = new Entry
        {
            Key = "Parameters.A", Content = "0.5", Unit = "factor", SourceQuote = "one-half the base rate",
            SourceUrls = ["https://example.org/a", "https://example.org/b"]
        };

        var result = SourceVerifier.Verify(entry,
        [
            ("https://example.org/a", Pages("Extra hours")),
            ("https://example.org/b", Pages("paid at one-half the base rate"))
        ]);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("example.org: Section \"Page 1\"", result.Location);
    }

    [Theory]
    // ReSharper disable StringLiteralTypo
    [InlineData("addi-\ntional", "additional")]
    [InlineData("addi- tional", "additional")]
    [InlineData("one-half", "onehalf")]
    // ReSharper restore StringLiteralTypo
    [InlineData("employee\u2019s \u201crate\u201d", "employee's \"rate\"")]
    [InlineData("  wages \n paid\t now ", "wages paid now")]
    [InlineData("Rate - 39%", "Rate - 39%")]
    public void Canonical(string text, string expected) =>
        Assert.Equal(expected, SourceVerifier.Canonical(text));
}
