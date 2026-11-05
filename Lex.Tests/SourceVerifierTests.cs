using Xunit;

namespace PayrollEngine.Lex.Tests;

public class SourceVerifierTests
{
    private static Entry Entry(string content, string unit = null) => new()
    {
        Key = "Test.2026",
        Content = content,
        Unit = unit,
        SourceUrls = ["https://example.org/doc"]
    };

    private static string Page(string body) =>
        "<html><body>" + body + "<h2>Notes</h2><p>" + string.Concat(Enumerable.Repeat("filler text ", 60)) + "</p></body></html>";

    [Fact]
    public void Numbers_NormalizesSeparators()
    {
        var numbers = SourceDocument.Numbers("Limit 12,570.50 and 13.518,68 and 7 500 and 0.45");
        Assert.Contains(12570.50m, numbers);
        Assert.Contains(13518.68m, numbers);
        Assert.Contains(7500m, numbers);
        Assert.Contains(0.45m, numbers);
    }

    [Fact]
    public void AllNumbersInOneSection_IsFound_WithLocation()
    {
        var document = SourceDocument.FromHtml(Page(
            "<h2>Rates</h2><p>The limit is 12,570 and the rate 40%.</p><h2>Other</h2><p>unrelated 99</p>"));
        var result = SourceVerifier.Verify(Entry("{\"Limit\":12570,\"Rate\":0.4}", "rate"), document);
        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Contains("Rates", result.Location);
    }

    [Fact]
    public void MissingNumber_IsPartial()
    {
        var document = SourceDocument.FromHtml(Page("<h2>Rates</h2><p>The limit is 12,570.</p>"));
        var result = SourceVerifier.Verify(Entry("{\"Limit\":12570,\"Other\":4321}"), document);
        Assert.Equal(SourceMatch.Partial, result.Match);
        Assert.Contains("4321", result.Missing);
    }

    [Fact]
    public void NoMatchingNumber_IsNotFound()
    {
        var document = SourceDocument.FromHtml(Page("<h2>Rates</h2><p>Nothing relevant here.</p>"));
        var result = SourceVerifier.Verify(Entry("{\"Limit\":12570}"), document);
        Assert.Equal(SourceMatch.NotFound, result.Match);
    }

    [Fact]
    public void ShortText_IsUnreadable()
    {
        var document = SourceDocument.FromHtml("<html>Loading</html>");
        var result = SourceVerifier.Verify(Entry("{\"Limit\":12570}"), document);
        Assert.Equal(SourceMatch.Unreadable, result.Match);
    }

    private static SourceDocument Sections(params string[] texts) =>
        new() { State = SourceState.Readable, Sections = texts.Select((x, i) => ($"Page {i + 1}", x)).ToList() };

    [Theory]
    [InlineData("%", true)]
    [InlineData("percent", true)]
    [InlineData("Rate", true)]
    [InlineData("fraction", true)]
    [InlineData("USD / rate", true)]
    [InlineData("% of salary", true)]
    [InlineData("INR; %", true)]
    [InlineData(null, false)]
    [InlineData("factor", false)]
    [InlineData("mixed", false)]
    [InlineData("coefficient", false)]
    [InlineData("USD/hour", false)]
    public void IsRateUnit(string unit, bool expected) =>
        Assert.Equal(expected, SourceVerifier.IsRateUnit(unit));

    [Fact]
    public void FactorUnit_IsNotLookedUpAsPercentage() =>
        Assert.Equal(SourceMatch.NotFound,
            SourceVerifier.Verify(Entry("0.5", "factor"), Sections("The workweek has 50 hours a week.")).Match);

    [Theory]
    [InlineData("The workweek has 50 hours a week.", 0)]
    [InlineData("The employee share is 4.25 %.", 1)]
    [InlineData("The employee share is 4.25%.", 1)]
    [InlineData("The employee share is 4.25 percent.", 1)]
    [InlineData("The employee share is 4.25 per cent.", 1)]
    public void RateUnit_PercentageNeedsPercentSign(string text, int found)
    {
        var result = SourceVerifier.Verify(Entry("0.0425", "rate"), Sections(text));
        Assert.Equal(found, result.NumbersFound);
    }

    [Fact]
    public void WithoutUnit_RateIsNotLookedUpAsPercentage() =>
        Assert.Equal(SourceMatch.NotFound,
            SourceVerifier.Verify(Entry("0.0425"), Sections("The employee share is 4.25%.")).Match);

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1.5, 2)]
    [InlineData(22, 2)]
    [InlineData(37, 2)]
    [InlineData(340, 2)]
    [InlineData(0.012, 2)]
    [InlineData(1000000, 1)]
    [InlineData(7.25, 3)]
    [InlineData(66150, 4)]
    public void SignificantDigits(double value, int expected) =>
        Assert.Equal(expected, SourceVerifier.SignificantDigits((decimal)value));

    [Fact]
    public void ShortSingleNumber_IsAmbiguous()
    {
        var result = SourceVerifier.Verify(Entry("22", "%"), Sections("The optional flat rate is 22%."));

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal("short number", result.Ambiguity);
        Assert.Null(result.Location);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public void TableWithManyNumbers_IsFound()
    {
        var result = SourceVerifier.Verify(Entry("[{\"Over\":0,\"Rate\":0.1},{\"Over\":19300,\"Rate\":0.12},{\"Over\":44100,\"Rate\":0.22}]", "mixed / rate"),
            Sections("Over 19,300 the rate is 10%; over 44,100 the rate is 12% and 22% above."));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Section \"Page 1\"", result.Location);
        Assert.True(result.CanWrite);
    }

    [Fact]
    public void NumberInManySections_IsAmbiguous()
    {
        var result = SourceVerifier.Verify(Entry("7.25"),
            Sections("Minimum wage 7.25", "Example: 7.25 per hour", "Table: 7.25", "Worksheet 7.25"));

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal("number in 4 sections", result.Ambiguity);
    }

    [Fact]
    public void NumberInFewSections_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("7.25"), Sections("Minimum wage 7.25", "Example: 7.25 per hour", "Table: 7.25")).Match);

    [Fact]
    public void Sample_IsEvenlyDistributed()
    {
        var numbers = Enumerable.Range(2, 100).Select(x => (decimal)x).ToList();

        var sample = SourceVerifier.Sample(numbers);

        Assert.Equal(40, sample.Count);
        Assert.Equal(2m, sample[0]);
        Assert.Equal(101m, sample[^1]);
        Assert.Equal(sample.Count, sample.Distinct().Count());
        Assert.Equal(sample, SourceVerifier.Sample(numbers));
    }

    [Fact]
    public void LargeTable_IsSampled()
    {
        var values = Enumerable.Range(1, 120).Select(x => 1000 + x * 7).ToList();
        var content = "[" + string.Join(",", values) + "]";

        var result = SourceVerifier.Verify(Entry(content), Sections(string.Join(" ", values)));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.True(result.Sampled);
        Assert.Equal(40, result.NumbersTotal);
        Assert.Equal(120, result.ContentNumbers);
    }

    [Theory]
    [InlineData("Section \"Page 24\"", true)]
    [InlineData("Sections \"Page 1\", \"Page 2\"", true)]
    [InlineData("US-2026-p15.pdf: Section \"Page 24\"", true)]
    [InlineData("Section \"SUMMARY:\"", true)]
    [InlineData("unemployment.example.org: Section \"Taxable wages\"; leave.example.org: Sections \"A\", \"B\"", true)]
    [InlineData("Page 24, section 7 \"supplemental wages\"", false)]
    [InlineData("example.org: Page 1, §207(a)(1) \"at a rate\"", false)]
    [InlineData("Table 3", false)]
    [InlineData("", false)]
    public void IsToolLocation(string location, bool expected) =>
        Assert.Equal(expected, SourceVerifier.IsToolLocation(location));
}
