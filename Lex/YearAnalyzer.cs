using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Year context of a number in a source text: years, tax years, citations and validity
/// </summary>
internal static class YearAnalyzer
{
    private const int MinYear = 1990;
    private const int MaxYear = 2100;

    /// <summary>
    /// Year of a validFrom date (yyyy-MM-dd), null when the date has no year in the range of <see cref="Years"/>
    /// </summary>
    private static int? ValidYear(string validFrom) =>
        validFrom is { Length: >= 4 } &&
        int.TryParse(validFrom.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
        year is >= MinYear and <= MaxYear
            ? year
            : null;

    /// <summary>
    /// Relevant period of an entry: the reference year of the entry, otherwise the year of validFrom. A validFrom
    /// on the 1st of January gives a calendar year; another day starts a tax year with the year of validFrom as start
    /// year (IN 2026-04-01: 2026-27, AU 2025-07-01: 2025-26, UK 2026-04-06: 2026-27); the reference year is then the
    /// start year of the period. Null without year.
    /// </summary>
    /// <param name="referenceYear">Reference year of the entry</param>
    /// <param name="validFrom">Start of the validity of the manifest (yyyy-MM-dd)</param>
    internal static RelevantPeriod? Period(int? referenceYear, string validFrom)
    {
        var year = referenceYear ?? ValidYear(validFrom);
        if (year == null)
        {
            return null;
        }
        var start = TaxYearStart(validFrom);
        return start == null
            ? new RelevantPeriod(year.Value, false)
            : new RelevantPeriod(year.Value, true, start.Value.Month, start.Value.Day);
    }

    /// <summary>Start of a tax year: validFrom when it is not the 1st of January</summary>
    private static DateOnly? TaxYearStart(string validFrom) =>
        validFrom is { Length: >= 10 } &&
        DateOnly.TryParseExact(validFrom[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var date) &&
        (date.Month != 1 || date.Day != 1)
            ? date
            : null;

    private static bool IsYear(string text) =>
        text.Length == 4 && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
        year is >= MinYear and <= MaxYear;

    /// <summary>Day and month of a numeric date before its year, with dots: 17.09.2026, 1.4.2025</summary>
    internal const string NumericDate = @"(?<![\d.,])(?:0?[1-9]|[12]\d|3[01])\.(?:0?[1-9]|1[0-2])\.";

    private static readonly Regex NumericDateBefore = new(NumericDate + "$", RegexOptions.Compiled);

    // four digits 1990–2100, not part of a longer number (12,025, 2025.5), an amount ($2000, Rs. 2000, CHF 2000,
    // 2000 €) or a percentage; the year of a numeric date with dots (17.09.2026) is a year
    private static readonly Regex YearToken = new(
        "(?:(?<=" + NumericDate + @")|(?<![\d.,$€£¥₹])(?<!(?:[$€£¥₹]|\bRs\.|\bCHF|\bFr\.|\bkr\.?)\s))" +
        @"(?:199\d|20\d\d|2100)(?!\d|[.,]\d|\s?(?:%|€|₹|(?:EUR|CHF)\b))",
        RegexOptions.Compiled);

    // a number row (wage bracket table: 1965 1995 32.50): another number on both sides, no word, label colon
    // or parenthesis in between (2025 (2024: 1.2%) keeps 2024)
    private const int RowWindow = 12;
    private static readonly Regex RowNumber = new(@"\d(?:[\d.,]*\d)?", RegexOptions.Compiled);
    private static readonly Regex RowGap = new(@"^[^\p{L}:()]*$", RegexOptions.Compiled);

    // validity start before the year (see LanguageSet.ValidityBefore)
    private const int ValidityWindow = 64;

    // a reference prefix directly before the year: Notice 2024-1, Pub. 2025, Rev. 2024, Rev. Guide 2024,
    // revision with month: Rev. 12-2025, Rev. December 2025
    private static readonly Regex ReferencePrefix = new(
        @"(?:\bNotice|\bPub\.|\bRev\.(?:\s?\d{1,2}\s?[-/]|\s?[A-Z][a-z]{2,8}\.?)?)\s?$", RegexOptions.Compiled);

    // a print or typesetting mark: a date with a time before (15:14 - 3-Dec-2025, 16:58 Oct 31, 2025)
    private static readonly Regex TimeStampBefore = new(
        @"\d{1,2}:\d{2}\s*[-\u2010-\u2015]?\s*(?:\d{1,2}\s*[-\u2010-\u2015\s]\s*)?[A-Z][a-z]{2,8}\.?\s*[-\u2010-\u2015\s]\s*(?:\d{1,2},?\s*)?$",
        RegexOptions.Compiled);

    // a print or typesetting mark: a year with a time after (Sep<11>2014 16:58)
    private static readonly Regex TimeStampAfter = new(@"^\s+\d{1,2}:\d{2}(?!\d)", RegexOptions.Compiled);

    // a document number: the year followed by a hyphen and up to five digits (2024-40, décret n° 2024-1234) or a
    // slash and a number (Regulation (EU) 2016/679); a full year after it is a range or a period (2024-2026, 2025/2026)
    private static readonly Regex DocumentNumberSuffix = new(
        @"^(?:[-\u2010\u2011\u2013](?<number>\d{1,5})|/(?<number>\d+))(?![\d/])", RegexOptions.Compiled);

    // a statute reference before a year in parentheses: Bill 951 (2022), L. 15-97 (2017), Act 32 (2020), § 12 (2017)
    private static readonly Regex StatuteBeforeParenthesis = new(
        @"(?:\b[A-Z][A-Z.]{0,5}|\bAct|\bLaw|\bBill|\bChapter|§)\s?\d[\d.\-]*[A-Za-z]?\s?\($", RegexOptions.Compiled);

    private const int ReferenceWindow = 32;

    // citations ----------------------------------------------------------------

    private const int CitationWindow = 96;

    // a margin note with the number of an enactment, the year followed by a punctuation mark: 43 of 1961. 30 of 2025;
    private static readonly Regex MarginNumberBefore = new(@"(?<![\p{L}\d.,])\d{1,3}\s+of\s+$", RegexOptions.Compiled);
    private static readonly Regex MarginNumberAfter = new("^[.;,)]", RegexOptions.Compiled);

    /// <summary>
    /// A year that names an enactment or a gazette issue (see <see cref="Years"/>); an enactment date that is
    /// a validity start (<c>mit Wirkung vom 1. Januar 2026</c>) is no citation
    /// </summary>
    private static bool IsCitation(string text, int index, int end, LanguageSet languages)
    {
        var before = text[Math.Max(0, index - CitationWindow)..index];
        if (languages.SlashNumberBefore.IsMatch(before) || languages.NumberOfBefore.IsMatch(before) ||
            languages.TitleBefore.IsMatch(before) || languages.GazetteBefore.IsMatch(before) ||
            (MarginNumberBefore.IsMatch(before) && MarginNumberAfter.IsMatch(text[end..Math.Min(text.Length, end + 1)])))
        {
            return true;
        }
        return languages.EnactmentDateBefore.IsMatch(before) &&
               !languages.ValidityBefore.IsMatch(text[Math.Max(0, index - ValidityWindow)..index]);
    }

    /// <summary>
    /// A document number after the year (<see cref="DocumentNumberSuffix"/>); a full year after the hyphen or slash
    /// is no document number
    /// </summary>
    private static bool IsDocumentNumber(string after)
    {
        var match = DocumentNumberSuffix.Match(after);
        return match.Success && !IsYear(match.Groups["number"].Value);
    }

    // periods -------------------------------------------------------------------

    // a tax year period after its start year: 2025-26, 2025–26, 2025/26 or 2025-2026, 2025/2026, 2025 to 2026
    private static readonly Regex PeriodSuffix = new(
        @"^(?:[-\u2010-\u2015/](?<short>\d{2})(?!\d|[-/.]\d)|" +
        @"(?:\s*[-\u2010-\u2015/]\s*|\s+to\s+)(?<long>199\d|20\d\d|2100)(?!\d|[.,]\d|[-/.]\d))",
        RegexOptions.Compiled);

    // a tax year prefix: FY 2025-26, financial year 2025-26, tax year 2025 to 2026, income year 2025–26
    private static readonly Regex TaxYearPrefix = new(
        @"(?<!\p{L})(?:FY|(?:[Ff]inancial|[Ff]iscal|[Tt]ax|[Ii]ncome)\s+[Yy]ear)\s?'?$", RegexOptions.Compiled);

    // an assessment year prefix, the year after the income period: AY 2026-27, assessment year 2026-27, YA 2027,
    // Year of Assessment 2027
    private static readonly Regex AssessmentPrefix = new(
        @"(?<!\p{L})(?:AY|YA|[Aa]ssessment\s+[Yy]ear|[Yy]ear\s+of\s+[Aa]ssessment)\s?$", RegexOptions.Compiled);

    // ReSharper disable once GrammarMistakeInComment
    // a tax year in two-digit form: FY26 (ends in 2026), FY 25-26, FY'26
    private static readonly Regex TaxYearShort = new(
        @"(?<!\p{L})FY\s?'?(?<end>\d{2})(?:[-\u2010-\u2015/](?<next>\d{2}))?(?![\d.,])", RegexOptions.Compiled);

    private const int PrefixWindow = 24;

    // a tax year by its start date before the year: assessment year commencing on the 1st day of April, 2026 (income
    // period 2025-26), previous year commencing on 1 April 2025, financial year beginning 1 July 2025
    private static readonly Regex CommencingPrefix = new(
        @"(?<!\p{L})(?:(?<assessment>AY|YA|[Aa]ssessment\s+[Yy]ear|[Yy]ear\s+of\s+[Aa]ssessment)|FY|" +
        @"(?:[Ff]inancial|[Ff]iscal|[Tt]ax|[Ii]ncome|[Pp]revious)\s+[Yy]ear)\s+(?:commencing|beginning|starting)\s+" +
        @"(?:on\s+|from\s+)?(?:the\s+)?$",
        RegexOptions.Compiled);

    private const int DateWindow = 48;

    /// <summary>
    /// Tax year period that starts with the year at the text position, null for a year: a short form
    /// (<c>2025-26</c>, <c>2025/26</c>) or a long form (<c>2025-2026</c>, <c>2025/2026</c>, <c>2025 to 2026</c>) of two
    /// consecutive years, optionally with a tax year prefix (<c>FY 2025-26</c>); an assessment year prefix rates the
    /// income period before (<c>AY 2026-27</c> and <c>YA 2027</c>: start year 2025 and 2026)
    /// </summary>
    private static YearHit PeriodHit(string text, int index, int end, int year, bool validity)
    {
        var before = text[Math.Max(0, index - PrefixWindow)..index];
        var assessment = AssessmentPrefix.IsMatch(before);
        var suffix = PeriodSuffix.Match(text[end..Math.Min(text.Length, end + 12)]);
        if (suffix.Success)
        {
            var next = suffix.Groups["short"].Success
                ? int.Parse(suffix.Groups["short"].Value, CultureInfo.InvariantCulture) == (year + 1) % 100
                : int.Parse(suffix.Groups["long"].Value, CultureInfo.InvariantCulture) == year + 1;
            if (next)
            {
                var start = assessment ? year - 1 : year;
                var plain = suffix.Groups["long"].Success && !assessment && !TaxYearPrefix.IsMatch(before);
                return new YearHit([start, start + 1], index, end + suffix.Length - index, validity, start, plain);
            }
        }
        return assessment ? new YearHit([year - 1, year], index, end - index, validity, year - 1) : null;
    }

    /// <summary>
    /// Tax years in two-digit form (<c>FY26</c>: 2025-26, <c>FY 25-26</c>)
    /// </summary>
    private static IEnumerable<YearHit> ShortTaxYears(string text)
    {
        foreach (Match match in TaxYearShort.Matches(text))
        {
            var end = 2000 + int.Parse(match.Groups["end"].Value, CultureInfo.InvariantCulture);
            if (match.Groups["next"].Success)
            {
                if (int.Parse(match.Groups["next"].Value, CultureInfo.InvariantCulture) != (end + 1) % 100)
                {
                    continue;
                }
                end++;
            }
            if (end - 1 is >= MinYear and < MaxYear)
            {
                yield return new YearHit([end - 1, end], match.Index, match.Length, periodStart: end - 1);
            }
        }
    }

    /// <summary>Maximum distance in characters between a number hit and the closest year</summary>
    internal const int YearWindow = 120;

    /// <summary>Years considered for the year context: from the relevant year minus this offset</summary>
    private const int YearsBefore = 3;

    /// <summary>Years considered for the year context: up to the relevant year plus this offset</summary>
    private const int YearsAfter = 1;

    /// <summary>
    /// Year context of a number hit, rated by the closest year (or year group, see <see cref="YearHits"/>) within
    /// <see cref="YearWindow"/> characters in both directions. Only years from the relevant year minus 3 to plus 1
    /// are considered; years of legal source references are ignored (see <see cref="Years"/>).
    /// <list type="bullet">
    /// <item><see cref="HitYear.Positive"/>: the closest year is the relevant year, or a group that holds it
    /// (at the same distance the relevant year wins)</item>
    /// <item><see cref="HitYear.Weak"/>: the closest year is another year</item>
    /// <item><see cref="HitYear.Neutral"/>: no year in the window, or the closest year starts a validity up to the
    /// relevant year (<c>effective January 1, 2025</c>, <c>2025 and after</c>) or names the year a value is unchanged
    /// from (<c>unchanged from 2024</c>, <c>same as 2024</c>): the value may still apply</item>
    /// </list>
    /// A year of the section heading counts as a year at the start of the section text.
    /// A number is year-weak when at least one hit is weak and no hit is positive.
    /// For a calendar year a tax year period is ignored, except a plain period of two full years
    /// (<c>2025-2026</c>), which is a year group. For a tax year starting in the relevant year Y:
    /// <list type="bullet">
    /// <item>a period starting in Y is <see cref="HitYear.Positive"/>, a period with another start year is
    /// <see cref="HitYear.Weak"/></item>
    /// <item>a year (or a group) holding Y or Y+1 is <see cref="HitYear.Neutral"/>: it lies in the period, but may
    /// also belong to the neighbouring period; a validity start up to Y is neutral too</item>
    /// <item>another year from Y−3 to Y+1 is <see cref="HitYear.Weak"/></item>
    /// </list>
    /// </summary>
    /// <param name="hit">Text position of the hit</param>
    /// <param name="years">Years, year groups and periods of the text with their position (<see cref="YearHits"/>)</param>
    /// <param name="period">Relevant period</param>
    internal static HitYear Classify((int Index, int Length) hit, IEnumerable<YearHit> years, RelevantPeriod period)
    {
        YearHit closest = null;
        var distance = int.MaxValue;
        foreach (var candidate in years)
        {
            if (!Considered(candidate, period))
            {
                continue;
            }
            var gap = candidate.Index >= hit.Index + hit.Length
                ? candidate.Index - (hit.Index + hit.Length)
                : hit.Index >= candidate.Index + candidate.Length
                    ? hit.Index - (candidate.Index + candidate.Length)
                    : -1;
            if (gap < 0 || gap > YearWindow)
            {
                // the year is the hit itself, or out of the window
                continue;
            }
            if (gap < distance || (gap == distance && Holds(candidate, period)))
            {
                distance = gap;
                closest = candidate;
            }
        }
        return closest == null ? HitYear.Neutral : Rate(closest, period);
    }

    /// <summary>
    /// A year hit within the considered years: from the relevant year minus 3 to plus 1, for a tax year period its
    /// start year; a period for a calendar year only as plain period
    /// </summary>
    private static bool Considered(YearHit candidate, RelevantPeriod period)
    {
        var year = period.Year;
        if (candidate.PeriodStart is { } start)
        {
            if (period.Fiscal)
            {
                return start >= year - YearsBefore && start <= year + YearsAfter;
            }
            if (!candidate.Plain)
            {
                return false;
            }
        }
        return candidate.Years.Any(y => y >= year - YearsBefore && y <= year + YearsAfter);
    }

    /// <summary>
    /// The year hit holds the relevant period: a calendar year within the year or group, a tax year as period start
    /// </summary>
    internal static bool Holds(YearHit candidate, RelevantPeriod period) =>
        period.Fiscal
            ? candidate.PeriodStart == period.Year || DatedTaxYear(candidate, period) == period.Year
            : (candidate.PeriodStart == null || candidate.Plain) && candidate.Years.Contains(period.Year);

    /// <summary>
    /// Start year of the tax year that holds a dated year (<c>January 2026</c>: 2025 for a tax year from April),
    /// null for a year without month, a group, a period or a month that does not decide
    /// </summary>
    private static int? DatedTaxYear(YearHit candidate, RelevantPeriod period) =>
        candidate.PeriodStart == null && candidate.Years.Count == 1 && candidate.Month is { } month
            ? period.TaxYearOf(candidate.Years[0], month, candidate.Day)
            : null;

    /// <summary>
    /// Rating of the closest year hit (see <see cref="YearAnalyzer.Classify"/>)
    /// </summary>
    private static HitYear Rate(YearHit closest, RelevantPeriod period)
    {
        var year = period.Year;
        var group = closest.PeriodStart == null || (!period.Fiscal && closest.Plain);
        if (closest.Validity && group && closest.Years.Max() <= year)
        {
            return HitYear.Neutral;
        }
        if (Holds(closest, period))
        {
            return HitYear.Positive;
        }
        if (period.Fiscal && DatedTaxYear(closest, period) != null)
        {
            return HitYear.Weak;
        }
        if (period.Fiscal && closest.PeriodStart == null && closest.Years.Any(y => y == year || y == year + 1))
        {
            return HitYear.Neutral;
        }
        return HitYear.Weak;
    }

    private const int MaxRangeYears = 10;

    /// <summary>
    /// Years of a text, used for the year context of a number hit and a source quote. A year is a four-digit
    /// number from 1990 to 2100 that is not part of a longer number, an amount (<c>$2000</c>, <c>Rs. 2000</c>,
    /// <c>2000 €</c>) or a percentage; digits of other scripts are read as ASCII digits (see <see cref="SourceScript"/>).
    /// Years that are part of a legal source reference are ignored, because they name the document, not the year of
    /// a value:
    /// <list type="bullet">
    /// <item>directly after <c>Notice</c>, <c>Pub.</c> or <c>Rev.</c>, also with a word (<c>Rev. Guide 2024</c>)
    /// or a revision with month (<c>Rev. 12-2025</c>, <c>Rev. December 2025</c>)</item>
    /// <item>in a document number with hyphen or slash (<c>2024-40</c>, <c>décret n° 2024-1234</c>,
    /// <c>Regulation (EU) 2016/679</c>); this also skips ISO dates (<c>2025-01-01</c>), a range of full years
    /// (<c>2024-2026</c>) is kept</item>
    /// <item>in parentheses after a statute reference (<c>Bill 951 (2022)</c>, <c>Act 32 (2020)</c>)</item>
    /// <item>after a number with slash (<c>No. 57/2023</c>, <c>BGBl. II Nr. 123/2025</c>, <c>Ley 35/2006</c>,
    /// <c>Real Decreto 1/2026</c>) or a number of a year (<c>Decree-Law No. 33 of 2021</c>)</item>
    /// <item>after an enactment title (<c>Finance Act, 2025</c>, <c>Income-tax Rules, 1962</c>,
    /// <c>Social Security Act 1991</c>) or an official gazette (<c>BGBl. I 2025 Nr. 12</c>, <c>Stb. 2024, 123</c>)</item>
    /// <item>in the date of an enactment (<c>Gesetz vom 20. Dezember 2024</c>, <c>loi du 23 décembre 2024</c>,
    /// <c>legge 30 dicembre 2024</c>), unless the date starts a validity</item>
    /// <item>in a print or typesetting mark, a date with a time (<c>15:14 - 3-Dec-2025</c>,
    /// <c>16:58 Oct 31, 2025</c>, <c>Sep 11 2014 16:58</c>): a document is printed before the year it is valid for</item>
    /// <item>in a number row, another number within 12 characters on both sides without a word, colon or
    /// parenthesis in between
    /// (wage bracket table <c>1965 1995 32.50</c>); years of an enumeration or range are no such number</item>
    /// </list>
    /// A citation that is not recognized counts as year, as does the year of a numeric date
    /// (<c>17.09.2026</c>, <c>01.01.2025-31.12.2025</c>), also within a number row. A validity start (<c>effective January 1, 2025</c>,
    /// <c>beginning on or after January 1, 2025</c>, <c>from 1 July 2025</c>, <c>2025 and after</c>,
    /// <c>ab dem 1. Januar 2025</c>) and the year of an unchanged value (<c>unchanged from 2024</c>,
    /// <c>same as 2024</c>, <c>unverändert seit 2024</c>) are kept but marked: up to the relevant year they rate a
    /// hit neutral. A year enumeration (<c>2024, 2025, or 2026</c>, <c>2024 and 2025</c>, <c>2024 und 2025</c>) and a
    /// year range (<c>2024-2026</c>, <c>2024 through 2026</c>, <c>2024 bis 2026</c>) are one year group.
    /// A tax year period (<c>2025-26</c>, <c>FY 2025-26</c>, <c>FY26</c>, <c>AY 2026-27</c>, <c>YA 2027</c>) is one
    /// hit with its income years (see <see cref="PeriodHit"/>). The words of the languages apply
    /// (see <see cref="LanguageSet"/>). A number with a year-weak hit and no positive hit is
    /// <see cref="SourceMatch.Ambiguous"/> (see <see cref="YearAnalyzer.Classify"/>);
    /// a source quote with the relevant year anchors the numbers it holds.
    /// </summary>
    /// <param name="text">Source text</param>
    /// <param name="languages">Languages, default: all languages</param>
    internal static HashSet<int> Years(string text, LanguageSet languages = null) =>
        YearHits(text, languages).SelectMany(x => x.Years).ToHashSet();

    /// <summary>
    /// Years of a text with their position, without the years of legal source references (see <see cref="Years"/>);
    /// an enumeration or a range of years is one group with all its years, a tax year period one hit with its
    /// start year
    /// </summary>
    /// <param name="text">Source text</param>
    /// <param name="languages">Languages, default: all languages</param>
    internal static List<YearHit> YearHits(string text, LanguageSet languages = null)
    {
        var years = new List<YearHit>();
        if (string.IsNullOrEmpty(text))
        {
            return years;
        }
        languages ??= LanguageSet.All;
        text = SourceScript.Normalize(text);
        var skipTo = 0;
        foreach (Match match in YearToken.Matches(text))
        {
            if (match.Index < skipTo)
            {
                // second year of a period
                continue;
            }
            var start = Math.Max(0, match.Index - ReferenceWindow);
            var before = text[start..match.Index];
            var end = match.Index + match.Length;
            var after = text[end..Math.Min(text.Length, end + 8)];
            if (ReferencePrefix.IsMatch(before) || TimeStampBefore.IsMatch(before) || TimeStampAfter.IsMatch(after) ||
                (after.StartsWith(')') && StatuteBeforeParenthesis.IsMatch(before)) ||
                IsCitation(text, match.Index, end, languages))
            {
                continue;
            }
            var validityBefore = text[Math.Max(0, match.Index - ValidityWindow)..match.Index];
            var validity = languages.ValidityBefore.IsMatch(validityBefore) ||
                           languages.UnchangedBefore.IsMatch(validityBefore) ||
                           languages.ValidityAfter.IsMatch(text[end..Math.Min(text.Length, end + 24)]);
            var value = int.Parse(match.Value, CultureInfo.InvariantCulture);
            var dateOffset = Math.Max(0, match.Index - DateWindow);
            var date = languages.Date(text[dateOffset..match.Index], out var dateStart);
            if (date != null)
            {
                var dateIndex = dateOffset + dateStart;
                var commencing = CommencingPrefix.Match(text[Math.Max(0, dateIndex - DateWindow)..dateIndex]);
                if (commencing.Success)
                {
                    var startYear = commencing.Groups["assessment"].Success ? value - 1 : value;
                    years.Add(new YearHit([startYear, startYear + 1], match.Index, match.Length, validity, startYear));
                    continue;
                }
            }
            var period = PeriodHit(text, match.Index, end, value, validity);
            if (period != null)
            {
                years.Add(period);
                skipTo = period.Index + period.Length;
                continue;
            }
            // a numeric date is no document number (01.01.2025-31.12.2025) and no number row (16.09.2026 ₹15,000)
            if (!NumericDateBefore.IsMatch(before) &&
                (IsDocumentNumber(after) || InNumberRow(text, match.Index, end, languages)))
            {
                continue;
            }
            var previous = years.Count > 0 && years[^1].PeriodStart == null ? years[^1] : null;
            var connector = previous != null ? text[(previous.Index + previous.Length)..match.Index] : null;
            if (connector != null && languages.YearEnumeration.IsMatch(connector))
            {
                years[^1] = previous.Join(value, match.Index + match.Length);
                continue;
            }
            if (connector != null && languages.YearRange.IsMatch(connector) && value > previous.Years[^1] &&
                value - previous.Years[^1] <= MaxRangeYears)
            {
                var join = previous;
                for (var y = previous.Years[^1] + 1; y <= value; y++)
                {
                    join = join.Join(y, match.Index + match.Length);
                }
                years[^1] = join;
                continue;
            }
            years.Add(new YearHit([value], match.Index, match.Length, validity, month: date?.Month, day: date?.Day));
        }
        var taxYears = ShortTaxYears(text).ToList();
        return taxYears.Count == 0 ? years : years.Concat(taxYears).OrderBy(x => x.Index).ToList();
    }

    /// <summary>
    /// A year candidate within a number row: another number within <see cref="RowWindow"/> characters on both
    /// sides without a word, colon or parenthesis in between; a year of an enumeration or range
    /// (<c>2024, 2025, 2026</c>) does not count
    /// </summary>
    private static bool InNumberRow(string text, int index, int end, LanguageSet languages) =>
        RowNeighbor(text[Math.Max(0, index - RowWindow)..index], before: true, languages) &&
        RowNeighbor(text[end..Math.Min(text.Length, end + RowWindow)], before: false, languages);

    private static bool RowNeighbor(string window, bool before, LanguageSet languages)
    {
        var numbers = RowNumber.Matches(window);
        if (numbers.Count == 0)
        {
            return false;
        }
        var number = before ? numbers[^1] : numbers[0];
        var gap = before ? window[(number.Index + number.Length)..] : window[..number.Index];
        if (!RowGap.IsMatch(gap))
        {
            return false;
        }
        var groupYear = number.Length == 4 &&
                        int.TryParse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
                        year is >= MinYear and <= MaxYear;
        return !(groupYear && (languages.YearEnumeration.IsMatch(gap) || languages.YearRange.IsMatch(gap)));
    }
}
