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
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("mixed", true)]
    [InlineData("%", true)]
    [InlineData("rate", true)]
    [InlineData("percent", true)]
    [InlineData("coefficient", true)]
    [InlineData("USD / rate", true)]
    [InlineData("% of salary", true)]
    [InlineData("factor", false)]
    [InlineData("Factor", false)]
    [InlineData("USD", false)]
    [InlineData("EUR", false)]
    [InlineData("hours", false)]
    [InlineData("days", false)]
    [InlineData("USD/hour", false)]
    public void AllowsPercent(string unit, bool expected) =>
        Assert.Equal(expected, SourceVerifier.AllowsPercent(unit));

    [Fact]
    public void FactorUnit_IsNotLookedUpAsPercentage() =>
        Assert.Equal(SourceMatch.NotFound,
            SourceVerifier.Verify(Entry("0.5", "factor"), Sections("The workweek has 50 hours a week.")).Match);

    [Fact]
    public void CurrencyUnit_IsNotLookedUpAsPercentage() =>
        Assert.Equal(SourceMatch.NotFound,
            SourceVerifier.Verify(Entry("0.0425", "USD"), Sections("The employee share is 4.25%.")).Match);

    [Theory]
    [InlineData("rate")]
    [InlineData("mixed")]
    [InlineData(null)]
    public void WithoutPercentSign_IsNotLookedUpAsPercentage(string unit) =>
        Assert.Equal(SourceMatch.NotFound,
            SourceVerifier.Verify(Entry("0.5", unit), Sections("The workweek has 50 hours a week.")).Match);

    [Theory]
    [InlineData("The employee share is 4.25 %.")]
    [InlineData("The employee share is 4.25%.")]
    [InlineData("The employee share is 4.25 percent.")]
    [InlineData("The employee share is 4.25 per cent.")]
    [InlineData("The employee share is 4.25 per centum.")]
    public void PercentSign_IsLookedUpAsPercentage(string text) =>
        Assert.Equal(1, SourceVerifier.Verify(Entry("0.0425", "rate"), Sections(text)).NumbersFound);

    [Fact]
    public void PerCentum_StatuteWording_IsFound()
    {
        var result = SourceVerifier.Verify(
            Entry("{\"FirstClaimLimit\":0.25,\"SecondClaimLimit\":0.6}", "mixed"),
            Sections("the part of the net earnings of a person for any week that is subject to a deduction order " +
                     "may not exceed 25 per centum of the net earnings for that week; for a second order of the same " +
                     "kind, the deduction may not exceed 60 per centum of such net earnings"));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Null(result.Ambiguity);
        Assert.Equal(2, result.NumbersFound);
        Assert.True(result.CanWrite);
    }

    [Fact]
    public void WithoutUnit_RateWithPercentSign_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("0.0425"), Sections("The employee share is 4.25%.")).Match);

    [Fact]
    public void MixedUnit_RateTableWithPercentSign_IsFound()
    {
        var result = SourceVerifier.Verify(
            Entry("[{\"Over\":0,\"Amount\":0,\"Rate\":0.1},{\"Over\":18000,\"Amount\":1800,\"Rate\":0.12},{\"Over\":42000,\"Amount\":4680,\"Rate\":0.22}]", "mixed"),
            Sections("$0 to $18,000: 10%; $18,000 to $42,000: $1,800.00 plus 12%; over $42,000: $4,680.00 plus 22%"));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal(result.NumbersTotal, result.NumbersFound);
    }

    [Theory]
    [InlineData("22", "%", "The optional flat rate is 22%.")]
    [InlineData("0.22", "mixed", "The optional flat rate is 22%.")]
    [InlineData("0.012", "percent", "The credit reduction is 1.2% for the year.")]
    [InlineData("1.5", "factor", "Extra hours at 1.5 the base rate.")]
    [InlineData("50", null, "Maximum 50 hours.")]
    public void ShortSingleNumber_IsAmbiguous(string content, string unit, string text)
    {
        var result = SourceVerifier.Verify(Entry(content, unit), Sections(text));

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal("short number", result.Ambiguity);
        Assert.Null(result.Location);
        Assert.False(result.CanWrite);
    }

    [Theory]
    [InlineData("4700", "mixed", "Multiply the number of units by $4,700.")]
    [InlineData("360", "mixed", "The monthly travel limit is $360.")]
    [InlineData("8900", "USD", "Enter $8,900 on line 4.")]
    public void SingleNumberWithThreeOrMoreDigits_IsFound(string content, string unit, string text)
    {
        var result = SourceVerifier.Verify(Entry(content, unit), Sections(text));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Section \"Page 1\"", result.Location);
    }

    [Fact]
    public void LongNumberInManySections_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("13700", "mixed"),
                Sections("Line 4: $13,700", "Worksheet 2: 13,700", "Example: subtract $13,700", "Table: 13,700")).Match);

    [Fact]
    public void TableWithManyNumbers_IsFound()
    {
        var result = SourceVerifier.Verify(Entry("[{\"Over\":0,\"Rate\":0.1},{\"Over\":18000,\"Rate\":0.12},{\"Over\":42000,\"Rate\":0.22}]", "mixed / rate"),
            Sections("Over 18,000 the rate is 10%; over 42,000 the rate is 12% and 22% above."));

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Section \"Page 1\"", result.Location);
        Assert.True(result.CanWrite);
    }

    [Fact]
    public void NumberInManySections_IsAmbiguous()
    {
        var result = SourceVerifier.Verify(Entry("8.15"),
            Sections("Hourly rate 8.15", "Example: 8.15 per hour", "Table: 8.15", "Worksheet 8.15"));

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal("number in 4 sections", result.Ambiguity);
    }

    [Theory]
    [InlineData("Hourly rate 8.15", 8.15, 3)]
    [InlineData("Limit $4,700 per year", 4700, 4)]
    [InlineData("Rate 1.2% of wages", 0.012, 2)]
    [InlineData("Rate 0.012 or 1.2%", 0.012, 2)]
    public void WrittenDigits_CountsDigitsAsWritten(string text, double value, int expected) =>
        Assert.Equal(expected, SourceNumbers.Scan(text).WrittenDigits((decimal)value, true));

    [Fact]
    public void NumberInFewSections_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("8.15"), Sections("Hourly rate 8.15", "Example: 8.15 per hour", "Table: 8.15")).Match);

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

    private const string ValidFrom = "2025-01-01";

    private const string YearTable =
        "Contribution rates by year. 2023: employee rate 0.31%, wage base $148,200. " +
        "2024: employee rate 0.27%, wage base $152,700. 2025: employee rate 0.19%, wage base $157,300.";

    private static Entry Quoted(string content, string quote) => new()
    {
        Key = "Test.2025",
        Content = content,
        Unit = "mixed",
        SourceQuote = quote,
        SourceUrls = ["https://example.org/doc"]
    };

    [Fact]
    public void MultiYearTable_ValueOfOtherYear_IsAmbiguous()
    {
        var result = SourceVerifier.Verify(Entry("{\"Rate\":0.0027,\"WageBase\":152700}", "mixed"),
            Sections(YearTable), ValidFrom);

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal(SourceVerifier.MultiYearAmbiguity + " (0.0027, 152700)", result.Ambiguity);
        Assert.Null(result.Location);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public void MultiYearTable_SingleNumber_IsAmbiguous()
    {
        var result = SourceVerifier.Verify(Entry("152700"), Sections(YearTable), ValidFrom);

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal(SourceVerifier.MultiYearAmbiguity, result.Ambiguity);
    }

    [Fact]
    public void MultiYearTable_WithoutValidFrom_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("{\"Rate\":0.0027,\"WageBase\":152700}", "mixed"), Sections(YearTable)).Match);

    [Fact]
    public void MultiYearTable_QuoteWithYear_IsFound()
    {
        var result = SourceVerifier.Verify(
            Quoted("{\"Rate\":0.0019,\"WageBase\":157300}", "2025: employee rate 0.19%, wage base $157,300"),
            Sections(YearTable), ValidFrom);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Null(result.Ambiguity);
        Assert.Equal("Section \"Page 1\"", result.Location);
        Assert.True(result.CanWrite);
    }

    [Fact]
    public void MultiYearTable_QuoteWithoutYear_IsAmbiguous()
    {
        var result = SourceVerifier.Verify(
            Quoted("{\"Rate\":0.0019,\"WageBase\":157300}", "employee rate 0.19%, wage base $157,300"),
            Sections(YearTable), ValidFrom);

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal(SourceVerifier.MultiYearAmbiguity + " (0.0019, 157300)", result.Ambiguity);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public void NumberAlsoInSingleYearSection_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("157300"), Sections(YearTable, "Wage base 2025: $157,300"), ValidFrom).Match);

    [Fact]
    public void SectionWithReferenceYear_IsNotMultiYear()
    {
        var result = SourceVerifier.Verify(Entry("24750"),
            Sections("Rev. Proc. 2024-40 sets the deferral limit for 2025 at $24,750."), ValidFrom);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Null(result.Ambiguity);
    }

    [Theory]
    [InlineData("Rev. Proc. 2024-40 sets the limit for 2025", new[] { 2025 })]
    [InlineData("Notice 2023-34 applies in 2025", new[] { 2025 })]
    [InlineData("see Pub. 2024 for the 2025 table", new[] { 2025 })]
    [InlineData("Rev. 2024 of the 2025 guide", new[] { 2025 })]
    [InlineData("SB 951 (2022) sets the 2025 rate", new[] { 2025 })]
    [InlineData("P.L. 115-97 (2017) applies in 2025", new[] { 2025 })]
    [InlineData("Document 2024-40, rate for 2025", new[] { 2025 })]
    [InlineData("Updated 2024-12-01: rate for 2025", new[] { 2025 })]
    [InlineData("Guide (Rev. 12- 2024) for 2025", new[] { 2025 })]
    [InlineData("Guide (Rev. December 2024) for 2025", new[] { 2025 })]
    [InlineData("/build07/source 15:14 - 3-Dec-2024 rates for 2025", new[] { 2025 })]
    [InlineData("Printed Sep<11>2014 16:58 Oct 31, 2024 Job 1234 rates for 2025", new[] { 2025 })]
    [InlineData("Effective January 1, 2024 to December 31, 2024; rate for 2025", new[] { 2024, 2025 })]
    [InlineData("Rates 2024 and 2025", new[] { 2024, 2025 })]
    [InlineData("Range 2024-2025", new[] { 2024, 2025 })]
    [InlineData("Rate for 2025 (2024: 1.2%)", new[] { 2024, 2025 })]
    [InlineData("Limit $2000, 2010 %, 12,025 and 2025.50 in 2025", new[] { 2025 })]
    [InlineData("Valid 1989 to 2101", new int[0])]
    public void Years_IgnoreReferenceYears(string text, int[] expected) =>
        Assert.Equal(expected.Order(), SourceVerifier.Years(text).Order());

    [Theory]
    [InlineData("Section \"Page 24\"", true)]
    [InlineData("Sections \"Page 1\", \"Page 2\"", true)]
    [InlineData("XX-2026-guide.pdf: Section \"Page 24\"", true)]
    [InlineData("Section \"SUMMARY:\"", true)]
    [InlineData("unemployment.example.org: Section \"Taxable wages\"; leave.example.org: Sections \"A\", \"B\"", true)]
    [InlineData("Page 24, section 7 \"additional payments\"", false)]
    [InlineData("example.org: Page 1, §12(a)(1) \"at a rate\"", false)]
    [InlineData("Table 3", false)]
    [InlineData("", false)]
    public void IsToolLocation(string location, bool expected) =>
        Assert.Equal(expected, SourceVerifier.IsToolLocation(location));
}
