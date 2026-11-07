using Xunit;

namespace PayrollEngine.Lex.Tests;

public class PeriodTests
{
    private const string India = "2026-04-01";
    private const string Australia = "2025-07-01";
    private const string Kingdom = "2026-04-06";
    private const string Calendar = "2026-01-01";

    private static Entry Entry(string content, int? referenceYear = null, string quote = null) => new()
    {
        Key = "Test.Period",
        Content = content,
        ReferenceYear = referenceYear,
        SourceQuote = quote,
        SourceUrls = ["https://example.org/doc"]
    };

    private static SourceDocument Sections(params string[] texts) =>
        new() { State = SourceState.Readable, Sections = texts.Select((x, i) => ($"Page {i + 1}", x)).ToList() };

    private static SourceMatch Verify(string content, string text, string validFrom, int? referenceYear = null) =>
        SourceVerifier.Verify(Entry(content, referenceYear), Sections(text), validFrom).Match;

    // period of the manifest ---------------------------------------------------

    [Theory]
    [InlineData(null, "2026-01-01", 2026, false)]
    [InlineData(null, "2026-04-01", 2026, true)]
    [InlineData(null, "2025-07-01", 2025, true)]
    [InlineData(null, "2026-04-06", 2026, true)]
    [InlineData(2025, "2026-04-01", 2025, true)]
    [InlineData(2025, "2026-01-01", 2025, false)]
    [InlineData(2025, null, 2025, false)]
    public void Period_FromValidFrom(int? referenceYear, string validFrom, int year, bool fiscal) =>
        Assert.Equal(new RelevantPeriod(year, fiscal), SourceVerifier.Period(referenceYear, validFrom));

    [Fact]
    public void Period_WithoutYear_IsNull() =>
        Assert.Null(SourceVerifier.Period(null, null));

    // recognition --------------------------------------------------------------

    [Theory]
    [InlineData("Rates 2025-26", 2025, false)]
    [InlineData("Rates 2025\u201326", 2025, false)]
    [InlineData("Rates 2025/26", 2025, false)]
    [InlineData("Rates 2099-00", 2099, false)]
    [InlineData("Rates 2025-2026", 2025, true)]
    [InlineData("Rates 2025/2026", 2025, true)]
    [InlineData("Rates 2025 to 2026", 2025, true)]
    [InlineData("Rates FY 2025-26", 2025, false)]
    [InlineData("Rates FY2025-26", 2025, false)]
    [InlineData("Rates for FY26", 2025, false)]
    [InlineData("Rates for FY 25-26", 2025, false)]
    [InlineData("financial year 2025-26", 2025, false)]
    [InlineData("tax year 2025 to 2026", 2025, false)]
    [InlineData("income year 2025\u201326", 2025, false)]
    [InlineData("AY 2026-27", 2025, false)]
    [InlineData("assessment year 2026-27", 2025, false)]
    [InlineData("YA 2027", 2026, false)]
    [InlineData("Year of Assessment 2027", 2026, false)]
    public void YearHits_Period(string text, int start, bool plain)
    {
        var hit = Assert.Single(SourceVerifier.YearHits(text));

        Assert.Equal(start, hit.PeriodStart);
        Assert.Equal([start, start + 1], hit.Years);
        Assert.Equal(plain, hit.Plain);
    }

    [Theory]
    [InlineData("Document 2024-40")]
    [InlineData("Notice 2025-26 applies")]
    [InlineData("Updated 2011-12-31")]
    [InlineData("Rates FY 25-27")]
    public void YearHits_NoPeriod_IsIgnored(string text) =>
        Assert.Empty(SourceVerifier.YearHits(text));

    [Theory]
    [InlineData("Valid 2024-2026", new[] { 2024, 2025, 2026 })]
    [InlineData("ya 2027", new[] { 2027 })]
    [InlineData("Tax year 2026", new[] { 2026 })]
    public void YearHits_YearOrGroup_IsNoPeriod(string text, int[] years)
    {
        var hit = Assert.Single(SourceVerifier.YearHits(text));

        Assert.Null(hit.PeriodStart);
        Assert.Equal(years, hit.Years);
    }

    // tax year rating ------------------------------------------------------------

    private const string FinancialYears = "Limit FY 2025-26: 1,50,000. Limit FY 2026-27: 1,75,000.";
    private const string AssessmentYears = "Limit AY 2026-27: 1,50,000. Limit AY 2027-28: 1,75,000.";

    [Theory]
    [InlineData(FinancialYears, "175000", SourceMatch.Found)]
    [InlineData(FinancialYears, "150000", SourceMatch.Ambiguous)]
    [InlineData(AssessmentYears, "175000", SourceMatch.Found)]
    [InlineData(AssessmentYears, "150000", SourceMatch.Ambiguous)]
    public void India_TaxYearTable(string text, string content, SourceMatch expected) =>
        Assert.Equal(expected, Verify(content, text, India));

    private const string IncomeYears = "Threshold $12,340 (2024\u201325); threshold $12,560 (2025\u201326).";

    [Theory]
    [InlineData(IncomeYears, "12560", SourceMatch.Found)]
    [InlineData(IncomeYears, "12340", SourceMatch.Ambiguous)]
    [InlineData("For the 2025-2026 income year the threshold is $12,560.", "12560", SourceMatch.Found)]
    public void Australia_TaxYearTable(string text, string content, SourceMatch expected) =>
        Assert.Equal(expected, Verify(content, text, Australia));

    [Theory]
    [InlineData("4567", SourceMatch.Found)]
    [InlineData("4321", SourceMatch.Ambiguous)]
    public void Kingdom_TaxYearTable(string content, SourceMatch expected) =>
        Assert.Equal(expected,
            Verify(content, "tax year 2025 to 2026: limit 4,321; tax year 2026 to 2027: limit 4,567", Kingdom));

    [Theory]
    [InlineData("In 2026 the limit is 1,75,000.", SourceMatch.Found)]
    [InlineData("In 2027 the limit is 1,75,000.", SourceMatch.Found)]
    [InlineData("In 2025 the limit is 1,75,000.", SourceMatch.Ambiguous)]
    [InlineData("In 2028 the limit is 1,75,000.", SourceMatch.Found)]
    public void TaxYear_SingleYear(string text, SourceMatch expected) =>
        Assert.Equal(expected, Verify("175000", text, India));

    [Theory]
    [InlineData("With effect from 1st April, 2025, the limit is 1,75,000.")]
    [InlineData("w.e.f. 1st April, 2025 the limit is 1,75,000.")]
    [InlineData("Applicable from 1 April 2025: limit 1,75,000.")]
    [InlineData("From 1 April 2025 the limit is 1,75,000.")]
    public void TaxYear_ValidityUpToPeriodStart_IsNeutral(string text) =>
        Assert.Equal(SourceMatch.Found, Verify("175000", text, India));

    [Fact]
    public void TaxYear_ReferenceYear_IsStartYear() =>
        Assert.Equal(SourceMatch.Found, Verify("150000", FinancialYears, India, referenceYear: 2025));

    [Theory]
    [InlineData("Limit FY 2026-27: 1,75,000", SourceMatch.Found)]
    [InlineData("Limit FY 2025-26: 1,75,000", SourceMatch.Ambiguous)]
    [InlineData("Limit: 1,75,000", SourceMatch.Found)]
    public void TaxYear_Quote_IsRatedWithinQuote(string quote, SourceMatch expected) =>
        Assert.Equal(expected,
            SourceVerifier.Verify(Entry("175000", quote: quote),
                Sections("Limit FY 2025-26: 1,75,000. Limit FY 2026-27: 1,75,000. Limit: 1,75,000."), India).Match);

    // calendar year rating -------------------------------------------------------

    [Theory]
    [InlineData("Limit FY 2025-26: 1,50,000.", SourceMatch.Found)]
    [InlineData("Limit AY 2026-27: 1,50,000.", SourceMatch.Found)]
    [InlineData("Limit 2025-26: 1,50,000.", SourceMatch.Found)]
    [InlineData("Limit YA 2027: 1,50,000.", SourceMatch.Found)]
    [InlineData("Limit 2026-2027: 1,50,000.", SourceMatch.Found)]
    [InlineData("Limit 2024-2025: 1,50,000.", SourceMatch.Ambiguous)]
    [InlineData("Limit 2024 to 2025: 1,50,000.", SourceMatch.Ambiguous)]
    public void CalendarYear_OnlyPlainPeriodsCount(string text, SourceMatch expected) =>
        Assert.Equal(expected, Verify("150000", text, Calendar));

    [Theory]
    [InlineData("From 1 July 2025 the rate is $8,600.", SourceMatch.Found)]
    [InlineData("With effect from 1 July 2025 the rate is $8,600.", SourceMatch.Found)]
    [InlineData("The rate of $8,600 is up from 2025.", SourceMatch.Ambiguous)]
    public void CalendarYear_FromWithDate_IsValidity(string text, SourceMatch expected) =>
        Assert.Equal(expected, Verify("8600", text, Calendar));
}
