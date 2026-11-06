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
    [InlineData("0.012", "percent", "The surcharge is 1.2% for the year.")]
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
        var result = SourceVerifier.Verify(Entry("{\"Rate\":0.0031,\"WageBase\":148200}", "mixed"),
            Sections(YearTable), ValidFrom);

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal(SourceVerifier.MultiYearAmbiguity + " (0.0031, 148200)", result.Ambiguity);
        Assert.Null(result.Location);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public void MultiYearTable_ValueOfValidYear_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("{\"Rate\":0.0019,\"WageBase\":157300}", "mixed"), Sections(YearTable), ValidFrom).Match);

    [Fact]
    public void MultiYearTable_SingleNumber_IsAmbiguous()
    {
        var result = SourceVerifier.Verify(Entry("0.0027"), Sections(YearTable), ValidFrom);

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal(SourceVerifier.MultiYearAmbiguity, result.Ambiguity);
    }

    [Fact]
    public void ValueNextToOtherYear_IsAmbiguous()
    {
        const string text = "The wage base increases to $54,800 in 2025 from $52,400 in 2024.";

        Assert.Equal(SourceMatch.Found, SourceVerifier.Verify(Entry("54800"), Sections(text), ValidFrom).Match);
        var previous = SourceVerifier.Verify(Entry("52400"), Sections(text), ValidFrom);
        Assert.Equal(SourceMatch.Ambiguous, previous.Match);
        Assert.Equal(SourceVerifier.MultiYearAmbiguity, previous.Ambiguity);
    }

    [Fact]
    public void TableRowWithOtherYearPeriod_IsAmbiguous()
    {
        var result = SourceVerifier.Verify(Entry("0.0007"),
            Sections("0.000615 0.0000 0.000315 0.0007 January 1, 2024 to December 31, 2024"), ValidFrom);

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal(SourceVerifier.MultiYearAmbiguity, result.Ambiguity);
    }

    [Theory]
    [InlineData(true, SourceMatch.Found)]
    [InlineData(false, SourceMatch.Ambiguous)]
    public void OtherYearFarFromValue_IsIgnored(bool far, SourceMatch expected)
    {
        var text = "Use this table with forms filed for 2019 or earlier. " +
                   (far ? string.Concat(Enumerable.Repeat("filler text ", 12)) : string.Empty) +
                   "The allowance amount is $3,900.";

        Assert.Equal(expected, SourceVerifier.Verify(Entry("3900"), Sections(text), "2020-01-01").Match);
    }

    [Theory]
    [InlineData("2022", SourceMatch.Found)]
    [InlineData("2028", SourceMatch.Found)]
    [InlineData("2023", SourceMatch.Ambiguous)]
    [InlineData("2027", SourceMatch.Ambiguous)]
    public void YearOutsideRange_IsIgnored(string year, SourceMatch expected) =>
        Assert.Equal(expected,
            SourceVerifier.Verify(Entry("8600"), Sections($"In {year} the limit was raised to $8,600."), "2026-01-01").Match);

    [Fact]
    public void NeutralHit_DoesNotConfirmWeakHit() =>
        Assert.Equal(SourceMatch.Ambiguous,
            SourceVerifier.Verify(Entry("52400"),
                Sections("2024 Taxable Wage Base: $52,400", "Taxable Wage Base | $52,400 | $54,800"), ValidFrom).Match);

    [Fact]
    public void OnlyNeutralHits_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("52400"), Sections("Taxable Wage Base | $52,400 | $54,800"), ValidFrom).Match);

    [Fact]
    public void PositiveHit_ConfirmsWeakHit() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("52400"),
                Sections("2024 Taxable Wage Base: $52,400", "2025 Taxable Wage Base: $52,400"), ValidFrom).Match);

    [Theory]
    [InlineData("Semimonthly $490.83 $736.25 $981.67 | Status Reported on 2024, 2025, or 2026 forms", SourceMatch.Found)]
    [InlineData("Rates for 2025 and 2026: semimonthly $736.25", SourceMatch.Found)]
    [InlineData("Valid 2024-2026: semimonthly $736.25", SourceMatch.Found)]
    [InlineData("Valid 2024 through 2026: semimonthly $736.25", SourceMatch.Found)]
    [InlineData("Valid 2023 through 2025: semimonthly $736.25", SourceMatch.Ambiguous)]
    [InlineData("Semimonthly $736.25 | Reported on 2024, 2025, or 2027 forms", SourceMatch.Ambiguous)]
    public void YearGroup_WithValidYear_IsPositive(string text, SourceMatch expected) =>
        Assert.Equal(expected, SourceVerifier.Verify(Entry("736.25"), Sections(text), "2026-01-01").Match);

    [Theory]
    [InlineData("Reported on 2024, 2025, or 2026 forms", new[] { 2024, 2025, 2026 })]
    [InlineData("Valid 2024 through 2026", new[] { 2024, 2025, 2026 })]
    [InlineData("Valid 2024\u20132026", new[] { 2024, 2025, 2026 })]
    public void YearHits_GroupEnumerationAndRange(string text, int[] expected) =>
        Assert.Equal(expected, Assert.Single(SourceVerifier.YearHits(text)).Years);

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
    public void MultiYearTable_QuoteWithoutYear_IsFound()
    {
        var result = SourceVerifier.Verify(
            Quoted("{\"Rate\":0.0031,\"WageBase\":148200}", "employee rate 0.31%, wage base $148,200"),
            Sections(YearTable), ValidFrom);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.True(result.CanWrite);
    }

    [Fact]
    public void MultiYearTable_QuoteWithOtherYear_IsAmbiguous()
    {
        var result = SourceVerifier.Verify(
            Quoted("{\"Rate\":0.0031,\"WageBase\":148200}", "2023: employee rate 0.31%, wage base $148,200"),
            Sections(YearTable), ValidFrom);

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal(SourceVerifier.MultiYearAmbiguity + " (0.0031, 148200)", result.Ambiguity);
        Assert.False(result.CanWrite);
    }

    [Theory]
    [InlineData("2024 and 2025: employee rate 0.31%, wage base $148,200")]
    [InlineData("2023 through 2025: employee rate 0.31%, wage base $148,200")]
    [InlineData("for 2023 and later years. Rate for 2025: employee rate 0.31%, wage base $148,200")]
    public void Quote_WithValidYearOrGroup_IsFound(string quote) =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Quoted("{\"Rate\":0.0031,\"WageBase\":148200}", quote),
                Sections("Contribution rates. " + quote + " Rates for 2023 and 2024 see archive."), ValidFrom).Match);

    [Fact]
    public void Quote_OnlyQuoteRated_SectionYearsIgnored() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Quoted("6400", "shall not exceed $6,400 ($3,200 in the case of a separate return"),
                Sections("(A) In general. The amount shall not exceed $6,400 ($3,200 in the case of a separate return). " +
                         "Amendments 2025—Act 12 substituted $6,400 ($3,200 for $4,800 ($2,400."),
                "2026-01-01").Match);

    // number rows -------------------------------------------------------------

    private const string WageBrackets =
        "Wages at least But less than 0 1 2 3\n" +
        "1935 1965 31.40 25.90 20.30 14.80 10.65 4.15 0.90\n" +
        "1965 1995 32.50 27.00 21.40 15.90 11.20 4.60 1.30\n" +
        "1995 2025 33.60 28.10 22.50 17.00 11.75 5.05 1.70\n" +
        "2025 2055 34.70 29.20 23.60 18.10 12.30 5.50 3.80\n";

    [Fact]
    public void NumberRow_AmountsAreNoYears() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("{\"A\":3.80,\"B\":10.65}", "mixed"), Sections(WageBrackets), "2026-01-01").Match);

    [Theory]
    [InlineData("1995 2025 33.60", new[] { 1995 })]
    [InlineData("1.70 - - - -\n2025 2055 34.70", new int[0])]
    [InlineData("Rates for 2024, 2025, 2026", new[] { 2024, 2025, 2026 })]
    [InlineData("$ 2025 wage", new int[0])]
    [InlineData("Rates 2025 0.0016 0.0007", new[] { 2025 })]
    [InlineData("0.0016 0.0007\n2025 rates", new[] { 2025 })]
    [InlineData("January 1, 2025 to December 31, 2025", new[] { 2025 })]
    [InlineData("Reported on 2024, 2025, or 2026 forms", new[] { 2024, 2025, 2026 })]
    public void Years_NumberRow(string text, int[] expected) =>
        Assert.Equal(expected.Order(), SourceVerifier.Years(text).Order());

    // validity ----------------------------------------------------------------

    [Theory]
    [InlineData("Rate Table Effective January 1, 2025 | Income Between | $0 – $2,400 | $2,400 – $4,600")]
    [InlineData("Effective 2025: brackets $2,400 and $4,600")]
    [InlineData("For tax years beginning on or after January 1, 2025, the brackets are $2,400 and $4,600")]
    [InlineData("(for tax year 2025 and after) Formula: wage - [$2,400 + (allowances x $4,600)]")]
    public void Validity_UpToValidYear_IsNeutral(string text) =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("{\"A\":2400,\"B\":4600}", "mixed"), Sections(text), "2026-01-01").Match);

    [Fact]
    public void Validity_AfterValidYear_IsWeak() =>
        Assert.Equal(SourceMatch.Ambiguous,
            SourceVerifier.Verify(Entry("{\"A\":2400,\"B\":4600}", "mixed"),
                Sections("Effective January 1, 2027 the brackets are $2,400 and $4,600"), "2026-01-01").Match);

    [Fact]
    public void Validity_IsNoPositiveEvidence() =>
        Assert.Equal(SourceMatch.Ambiguous,
            SourceVerifier.Verify(Entry("{\"A\":2400,\"B\":4600}", "mixed"),
                Sections("Effective January 1, 2026 the brackets are $2,400 and $4,600",
                         "Legislation of the 2025 session sets the brackets $2,400 and $4,600"), "2026-01-01").Match);

    [Fact]
    public void NumberAlsoNextToValidYear_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            SourceVerifier.Verify(Entry("148200"), Sections(YearTable, "Wage base 2025: $148,200"), ValidFrom).Match);

    [Fact]
    public void SectionWithReferenceYear_IsNotMultiYear()
    {
        var result = SourceVerifier.Verify(Entry("21600"),
            Sections("Circular 2024-40 sets the savings limit for 2025 at $21,600."), ValidFrom);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Null(result.Ambiguity);
    }

    [Theory]
    [InlineData("Circular 2024-40 sets the limit for 2025", new[] { 2025 })]
    [InlineData("Rev. Guide 2024 sets the limit for 2025", new[] { 2025 })]
    [InlineData("Notice 2023-34 applies in 2025", new[] { 2025 })]
    [InlineData("see Pub. 2024 for the 2025 table", new[] { 2025 })]
    [InlineData("Rev. 2024 of the 2025 guide", new[] { 2025 })]
    [InlineData("Bill 951 (2022) sets the 2025 rate", new[] { 2025 })]
    [InlineData("L. 15-97 (2017) applies in 2025", new[] { 2025 })]
    [InlineData("§ 12 (2017) applies in 2025", new[] { 2025 })]
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
