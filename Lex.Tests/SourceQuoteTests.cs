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
            "If the supple-\nmental wages paid to the employee\nduring the calendar year exceed $1 million, withhold at 37%.");

        var result = SourceVerifier.Verify(
            Quoted("1000000", "If the supplemental wages paid to the employee during the calendar year exceed $1 million"), document);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Section \"Page 2\"", result.Location);
        Assert.True(result.CanWrite);
    }

    [Fact]
    public void QuoteWithCopiedHyphenation_IsFound() =>
        Assert.Equal(SourceMatch.Found, SourceVerifier.Verify(
            Quoted("1000000", "the supple- mental wages exceed $1 million"),
            Pages("If the supplemental wages exceed $1 million, withhold at 37%.")).Match);

    [Fact]
    public void QuoteWithTypographicQuotes_IsFound() =>
        Assert.Equal(SourceMatch.Found, SourceVerifier.Verify(
            Quoted("0.5", "not less than one-half the employee's \"regular rate\"", "factor"),
            Pages("paid at a rate of not less than one-half the employee\u2019s \u201cregular rate\u201d of pay")).Match);

    [Fact]
    public void FactorInWordsWithinQuote_IsFound()
    {
        var result = SourceVerifier.Verify(
            Quoted("1.5", "at a rate not less than one and one-half times the regular rate", "factor"),
            Pages("Overtime in excess of forty hours is paid at a rate not less than one and one-half times the regular rate."));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Section \"Page 1\"", result.Location);
    }

    [Fact]
    public void QuoteFound_NumberInOtherSection_IsNotFound()
    {
        var result = SourceVerifier.Verify(
            Quoted("7.25", "employees shall be paid the minimum wage"),
            Pages("employees shall be paid the minimum wage stated in the table", "Table: minimum wage 7.25 an hour"));

        Assert.Equal(SourceMatch.NotFound, result.Match);
        Assert.Equal(["7.25"], result.Missing);
        Assert.Null(result.Location);
    }

    [Fact]
    public void QuoteMissing_IsQuoteNotFound()
    {
        var result = SourceVerifier.Verify(
            Quoted("7.25", "the minimum wage is seven dollars"),
            Pages("The minimum wage is 7.25 an hour."));

        Assert.Equal(SourceMatch.QuoteNotFound, result.Match);
        Assert.Equal("the minimum wage is seven dollars", result.Quote);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public void QuoteInSecondSource_IsFound()
    {
        var entry = new Entry
        {
            Key = "Parameters.A", Content = "0.5", Unit = "factor", SourceQuote = "one-half the regular rate",
            SourceUrls = ["https://example.org/a", "https://example.org/b"]
        };

        var result = SourceVerifier.Verify(entry,
        [
            ("https://example.org/a", Pages("Overtime rules")),
            ("https://example.org/b", Pages("paid at one-half the regular rate"))
        ]);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("example.org: Section \"Page 1\"", result.Location);
    }

    [Theory]
    [InlineData("supple-\nmental", "supplemental")]
    [InlineData("supple- mental", "supplemental")]
    // ReSharper disable once StringLiteralTypo
    [InlineData("one-half", "onehalf")]
    [InlineData("employee\u2019s \u201crate\u201d", "employee's \"rate\"")]
    [InlineData("  wages \n paid\t now ", "wages paid now")]
    [InlineData("Rate - 37%", "Rate - 37%")]
    public void Canonical(string text, string expected) =>
        Assert.Equal(expected, SourceVerifier.Canonical(text));
}
