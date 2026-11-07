using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
// ReSharper disable CommentTypo

namespace PayrollEngine.Lex;

/// <summary>
/// Verification state of one lex entry against its source
/// </summary>
public enum SourceMatch
{
    /// <summary>All numbers of the entry are found in the source</summary>
    Found,

    /// <summary>All numbers are found, but the evidence is weak: the entry holds a single number that is written
    /// with two digits at most (22%, 1.5), is written with three digits at most and occurs in many sections,
    /// or is found in words only; or a number of the entry has a hit closest to another year and no hit closest
    /// to the relevant year (reference year of the entry or year of validFrom; e.g. a table over several years).
    /// Confirm the value manually (LexAttest) or anchor it with a sourceQuote.</summary>
    Ambiguous,

    /// <summary>Some numbers are found, others are not</summary>
    Partial,

    /// <summary>None of the numbers is found</summary>
    NotFound,

    /// <summary>The sourceQuote of the entry is not found in any readable source</summary>
    QuoteNotFound,

    /// <summary>No source can be read (blocked host, script-only page, scanned PDF)</summary>
    Unreadable,

    /// <summary>The entry holds no numbers to look for</summary>
    NoNumbers,

    /// <summary>The content of the entry cannot be read (content file missing or not JSON)</summary>
    NoContent,

    /// <summary>The entry has no local evidence file for any source (local verification)</summary>
    NoEvidence
}

/// <summary>
/// Year context of a number hit (see <see cref="SourceVerifier.YearContext"/>)
/// </summary>
internal enum HitYear
{
    /// <summary>No year within the window</summary>
    Neutral,

    /// <summary>The closest year is the relevant year, or a year group that holds it</summary>
    Positive,

    /// <summary>The closest year is another year</summary>
    Weak
}

/// <summary>
/// Relevant period of the year context: a calendar year, or a tax year that starts in the year (validFrom not on
/// the 1st of January: IN 2026-04-01 is the tax year 2026-27)
/// </summary>
/// <param name="Year">Relevant year: the reference year of the entry or the year of validFrom; the start year of a tax year</param>
/// <param name="Fiscal">The period is a tax year starting in <paramref name="Year"/></param>
internal readonly record struct RelevantPeriod(int Year, bool Fiscal);

/// <summary>
/// Year, year group (enumeration, range) or tax year period of a text with its position
/// </summary>
internal sealed class YearHit(List<int> years, int index, int length, bool validity = false,
    int? periodStart = null, bool plain = false)
{
    /// <summary>Years of the hit, one year or the years of a group; start and end year of a period</summary>
    internal List<int> Years => years;

    /// <summary>Text position of the first year</summary>
    internal int Index => index;

    /// <summary>Text length up to the end of the last year</summary>
    internal int Length => length;

    /// <summary>Start of a validity (<c>effective January 1, 2025</c>, <c>2025 and after</c>) or a value
    /// unchanged from the year (<c>unchanged from 2024</c>)</summary>
    internal bool Validity => validity;

    /// <summary>Start year of a tax year period (<c>2025-26</c>, <c>FY 2025-26</c>, <c>AY 2026-27</c>: 2025),
    /// null for a year or a year group</summary>
    internal int? PeriodStart => periodStart;

    /// <summary>A period of two full consecutive years without prefix (<c>2025-2026</c>, <c>2025/2026</c>,
    /// <c>2025 to 2026</c>): a year group for a calendar year</summary>
    internal bool Plain => plain;

    /// <summary>Group with a further year that ends at the text position</summary>
    internal YearHit Join(int year, int end) => new([.. years, year], index, end - index, validity);

    /// <summary>The year at the start of the section text (year of the section heading)</summary>
    internal YearHit AtStart() => new(years, 0, 0, validity, periodStart, plain);
}

/// <summary>
/// Result for one entry
/// </summary>
public sealed class SourceResult
{
    /// <summary>Key of the lex entry</summary>
    public string Key { get; internal init; }

    /// <summary>Source URLs of the lex entry</summary>
    public List<string> Urls { get; internal init; } = [];

    /// <summary>Verification state</summary>
    public SourceMatch Match { get; internal init; }

    /// <summary>Numbers of the entry that were looked up (a sample of a large table)</summary>
    public int NumbersTotal { get; internal init; }

    /// <summary>All numbers of the entry content; more than <see cref="NumbersTotal"/> when sampled</summary>
    public int ContentNumbers { get; internal init; }

    /// <summary>The numbers of a large table were sampled</summary>
    public bool Sampled => ContentNumbers > NumbersTotal;

    /// <summary>Numbers found in the source</summary>
    public int NumbersFound { get; internal init; }

    /// <summary>Numbers missing in the source</summary>
    public List<string> Missing { get; internal init; } = [];

    /// <summary>Proposed source location (sections that hold the numbers)</summary>
    public string Location { get; internal init; }

    /// <summary>Reason of an unreadable source; with several sources, the unreadable ones; reason of missing content</summary>
    public string Reason { get; internal init; }

    /// <summary>Why the evidence of an ambiguous entry is weak</summary>
    public string Ambiguity { get; internal init; }

    /// <summary>Source quote of the entry</summary>
    public string Quote { get; internal init; }

    /// <summary>Local evidence file of the entry (first source with a local file), null for a live verification</summary>
    public string Evidence { get; internal set; }

    /// <summary>Languages of the verification with the hash of their profiles (<see cref="LanguageSet.Label"/>)</summary>
    public string Languages { get; internal set; }

    /// <summary>Reason of the autoVerification exclusion that matches the entry, null when not excluded</summary>
    public string ExcludedReason { get; internal set; }

    /// <summary>Derived properties of the entry (exclusion kind derived); their numbers are not looked up</summary>
    public List<DerivedValue> Derived { get; internal init; } = [];

    /// <summary>Property paths of a bundle entry with the value 0; their numbers are not looked up</summary>
    public List<string> Zero { get; internal init; } = [];

    /// <summary>Zero properties of the entry with a notApplicable exclusion</summary>
    public List<DerivedValue> NotApplicable { get; internal init; } = [];

    /// <summary>Zero properties without justification: neither a notApplicable nor a derived exclusion</summary>
    public List<string> UnjustifiedZero =>
        DerivedProperties.Unjustified(Zero, NotApplicable.Concat(Derived).Select(x => x.Property));

    /// <summary>Hint for the review of the result, e.g. all numbers of the entry are derived</summary>
    public string Note { get; internal init; }

    /// <summary>The source location and the automatic verification record may be written</summary>
    public bool CanWrite => Match == SourceMatch.Found && ExcludedReason == null;
}

/// <summary>
/// Looks up the numbers of lex entries in their source documents
/// </summary>
public static class SourceVerifier
{
    private const int MaxNumbers = 40;
    private const int MaxSectionsPerSource = 3;

    /// <summary>A single number written with this many digits or less is weak evidence (22%, 1.5, 50)</summary>
    private const int WeakDigits = 2;

    /// <summary>A single number written with this many digits or less that occurs in more than
    /// <see cref="WeakSections"/> sections is weak evidence</summary>
    private const int FrequentDigits = 3;

    /// <summary>A single number that occurs in more sections is weak evidence</summary>
    private const int WeakSections = 3;

    /// <summary>Ambiguity of a year-weak number (see <see cref="YearContext"/>)</summary>
    internal const string MultiYearAmbiguity = "multi-year section: add sourceQuote with the year";

    /// <summary>Note of an entry whose numbers are all derived</summary>
    internal const string AllDerivedNote = "all numbers derived: confirm with LexAttest";

    /// <summary>Hint for zero properties of a bundle without a notApplicable exclusion</summary>
    // ReSharper disable once UnusedMember.Global
    public const string UnjustifiedZeroHint = "zero values without justification";

    /// <summary>Note of a found entry whose location exceeds the maximum location length</summary>
    internal const string LocationTooLongNote = "location too long: set sourceLocation manually";

    /// <summary>
    /// Verification method of an automatic verification (tool and version), e.g. LexVerifySource 1.2.0
    /// </summary>
    public static string Method { get; } =
        $"LexVerifySource {typeof(SourceVerifier).Assembly.GetName().Version?.ToString(3) ?? "1.0"}";

    /// <summary>
    /// Automatic verification record for an entry whose numbers were all found
    /// </summary>
    /// <param name="verifiedOn">Verification date</param>
    /// <param name="evidence">Local evidence file name (declared in files[]), null for a live verification</param>
    /// <param name="derived">Derived properties of the entry, not taken from the source</param>
    /// <param name="languages">Languages of the verification (<see cref="SourceResult.Languages"/>), part of the
    /// method: <c>LexVerifySource 1.3.0; languages de,en 3f2a9c1b</c></param>
    public static Verification AutoVerification(DateOnly verifiedOn, string evidence, IEnumerable<string> derived = null,
        string languages = null)
    {
        var properties = derived?.ToList();
        return new()
        {
            Mode = Verification.ModeAuto,
            VerifiedOn = verifiedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Method = string.IsNullOrWhiteSpace(languages) ? Method : $"{Method}; languages {languages}",
            Evidence = evidence,
            Derived = properties is { Count: > 0 } ? properties : null
        };
    }

    /// <summary>
    /// Numbers of an entry content (scalars, object properties, array items), sorted; 0, 1 and open upper bounds
    /// (nines only, see <see cref="IsPlaceholder"/>) carry no information
    /// </summary>
    internal static List<decimal> ContentNumbers(string content) =>
        ContentNumbers(content, [], out _);

    /// <summary>
    /// Numbers of an entry content without the numbers of the derived properties; a number that is also
    /// in a property that is not derived remains
    /// </summary>
    /// <param name="content">Entry content</param>
    /// <param name="derived">Property paths of the derived properties</param>
    /// <param name="derivedNumbers">Numbers of the derived properties</param>
    private static List<decimal> ContentNumbers(string content, IReadOnlyCollection<string> derived, out int derivedNumbers)
    {
        derivedNumbers = 0;
        var numbers = new HashSet<decimal>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(content);
            if (derived.Count == 0)
            {
                Collect(document.RootElement, numbers);
            }
            else
            {
                var derivedValues = new HashSet<decimal>();
                Collect(document.RootElement, numbers, string.Empty,
                    derived.ToHashSet(StringComparer.OrdinalIgnoreCase), derivedValues);
                derivedNumbers = derivedValues.Count;
            }
        }
        catch (JsonException)
        {
            Add(numbers, content);
        }
        return numbers.OrderBy(x => x).ToList();
    }

    /// <summary>
    /// Deterministic sample of sorted numbers: all numbers up to the maximum, otherwise numbers evenly
    /// distributed over the whole range, including the smallest and the largest
    /// </summary>
    internal static List<decimal> Sample(List<decimal> numbers, int max = MaxNumbers)
    {
        if (numbers.Count <= max)
        {
            return numbers;
        }
        var sample = new List<decimal>(max);
        for (var i = 0; i < max; i++)
        {
            sample.Add(numbers[(int)((long)i * (numbers.Count - 1) / (max - 1))]);
        }
        return sample;
    }

    /// <summary>
    /// Collect the numbers of a JSON element; the numbers of a derived property go to <paramref name="derivedNumbers"/>.
    /// The property path runs over objects only (null below an array: no derived property).
    /// </summary>
    private static void Collect(JsonElement element, HashSet<decimal> numbers, string path = null,
        HashSet<string> derived = null, HashSet<decimal> derivedNumbers = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var value)) Consider(numbers, value);
                break;
            case JsonValueKind.String:
                Add(numbers, element.GetString());
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var propertyPath = path == null ? null : path.Length == 0 ? property.Name : path + "." + property.Name;
                    if (propertyPath != null && derived != null && derived.Contains(propertyPath))
                    {
                        Collect(property.Value, derivedNumbers);
                        continue;
                    }
                    Collect(property.Value, numbers, propertyPath, derived, derivedNumbers);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, numbers);
                break;
        }
    }

    private static void Add(HashSet<decimal> numbers, string text)
    {
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            Consider(numbers, value);
        }
    }

    private static void Consider(HashSet<decimal> numbers, decimal value)
    {
        value = Math.Abs(value);
        // ReSharper disable once MergeIntoPattern
        if ((value > 1m || (value > 0m && value < 1m)) && !IsPlaceholder(value))
        {
            numbers.Add(value);
        }
    }

    private const int MinPlaceholderDigits = 6;

    /// <summary>
    /// Open upper bound of a range, written as nines only (999999, 99999999.99): not a value of the source
    /// </summary>
    internal static bool IsPlaceholder(decimal value)
    {
        var whole = Math.Truncate(value).ToString(CultureInfo.InvariantCulture);
        if (whole.Length < MinPlaceholderDigits || whole.Any(c => c != '9'))
        {
            return false;
        }
        var fraction = value.ToString(CultureInfo.InvariantCulture);
        var point = fraction.IndexOf('.');
        if (point < 0)
        {
            return true;
        }
        fraction = fraction[(point + 1)..];
        return fraction.All(c => c == '9') || fraction.All(c => c == '0');
    }

    private static readonly string[] NonRateTokens = ["factor", "hour", "hours", "day", "days"];

    private static readonly Regex UnitToken = new("%|[A-Za-z]+", RegexOptions.Compiled);

    private static readonly Regex CurrencyCode = new("^[A-Z]{3}$", RegexOptions.Compiled);

    private static readonly string[] PercentTokens = ["%", "percent", "percentage"];

    /// <summary>
    /// Lookup of a value below 1 as percentage (0.085 as 8.5%, the source writes a percent sign): always, except
    /// for units that are no rate — <c>factor</c>, <c>hours</c>, <c>days</c> or a currency code (<c>USD</c>,
    /// <c>EUR</c>); a combined unit like <c>USD / rate</c> allows it. The unit <c>percent points</c> (also
    /// <c>percentage points</c>, <c>% points</c>) declares a source that writes the rate as percentage without
    /// percent sign, a rate table under a heading (<c>Tipos de cotización</c>: 2,60 for 0.026)
    /// </summary>
    internal static PercentLookup Percent(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return PercentLookup.Sign;
        }
        var tokens = UnitToken.Matches(unit).Select(x => x.Value).ToList();
        if (tokens.Contains("points", StringComparer.OrdinalIgnoreCase) &&
            tokens.Any(t => PercentTokens.Contains(t, StringComparer.OrdinalIgnoreCase)))
        {
            return PercentLookup.Points;
        }
        return tokens.Count == 0 || !tokens.All(t =>
            CurrencyCode.IsMatch(t) || NonRateTokens.Contains(t, StringComparer.OrdinalIgnoreCase))
            ? PercentLookup.Sign
            : PercentLookup.None;
    }

    // a location written by LexVerifySource: Section "…" or Sections "…", "…", optionally with a source prefix
    private static readonly Regex ToolLocation = new(
        @"^(?:[^"":;]+: )?Sections? ""[^""]*""(?:, ""[^""]*"")*(?:; [^"":;]+: Sections? ""[^""]*""(?:, ""[^""]*"")*)*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Test if a source location was written by LexVerifySource (and may be refreshed):
    /// <c>Section "Page 24"</c>, <c>Sections "Page 1", "Page 2"</c>, optionally prefixed with the evidence file
    /// or, without evidence file name, the host (<c>guide.pdf: Section "Page 24"</c>, several sources separated
    /// by <c>; </c>: <c>guide.pdf: Section "Page 24"; limits.pdf: Section "Page 1"</c>)
    /// </summary>
    public static bool IsToolLocation(string location) =>
        !string.IsNullOrWhiteSpace(location) && ToolLocation.IsMatch(location.Trim());

    /// <summary>
    /// Verify one entry against the document of its single source
    /// </summary>
    /// <param name="entry">Lex entry</param>
    /// <param name="document">Source document</param>
    /// <param name="validFrom">Start of the validity of the manifest (yyyy-MM-dd), null without year context</param>
    /// <param name="languages">Languages of the number and year words, default: all available languages</param>
    public static SourceResult Verify(Entry entry, SourceDocument document, string validFrom = null,
        LanguageSet languages = null) =>
        Verify(entry, [(entry.Urls.FirstOrDefault(), document)], validFrom: validFrom, languages: languages);

    /// <summary>
    /// Verify one entry against the documents of its sources: a number is found when any readable
    /// document holds it. With a source quote, the numbers must be in the section that holds the quote.
    /// </summary>
    /// <param name="entry">Lex entry</param>
    /// <param name="sources">Source URL and document, in the order of the entry source URLs</param>
    /// <param name="content">Content to verify, default: the inline content of the entry (the text of a content file)</param>
    /// <param name="exclusions">Exclusions of the manifest; the numbers of the properties addressed by a derived
    /// exclusion of the entry are not looked up, a notApplicable exclusion justifies a zero property</param>
    /// <param name="validFrom">Start of the validity of the manifest (yyyy-MM-dd); its year, or the reference year of
    /// the entry, rates a year-weak number as <see cref="SourceMatch.Ambiguous"/> (see <see cref="YearContext"/>);
    /// a validFrom not on the 1st of January starts a tax year with this start year (see <see cref="Period"/>),
    /// null without year context</param>
    /// <param name="languages">Languages of the number and year words, default: all available languages</param>
    public static SourceResult Verify(Entry entry, IReadOnlyList<(string Url, SourceDocument Document)> sources,
        string content = null, IReadOnlyList<Exclusion> exclusions = null, string validFrom = null,
        LanguageSet languages = null)
    {
        languages ??= LanguageSet.All;
        var result = VerifyEntry(entry, sources, content, exclusions, validFrom, languages);
        result.Languages = languages.Label;
        return result;
    }

    private static SourceResult VerifyEntry(Entry entry, IReadOnlyList<(string Url, SourceDocument Document)> sources,
        string content, IReadOnlyList<Exclusion> exclusions, string validFrom, LanguageSet languages)
    {
        var period = Period(entry.ReferenceYear, validFrom);
        var urls = entry.Urls.ToList();
        content ??= entry.Content;
        var derived = DerivedProperties.Values(entry.Key, content, exclusions);
        var notApplicable = DerivedProperties.Values(entry.Key, content, exclusions, Exclusion.KindNotApplicable);
        var zero = DerivedProperties.Zeros(content);
        var readable = sources.Where(s => s.Document.State == SourceState.Readable).ToList();
        var failed = sources.Where(s => s.Document.State != SourceState.Readable).ToList();
        var reason = sources.Count == 1
            ? failed.FirstOrDefault().Document?.Reason
            : failed.Count == 0 ? null
            : string.Join("; ", failed.Select(s => $"{Label(s.Url, s.Document)}: {s.Document.Reason}"));
        if (readable.Count == 0)
        {
            return new SourceResult
            {
                Key = entry.Key, Urls = urls, Reason = reason, Derived = derived, Zero = zero, NotApplicable = notApplicable,
                Match = failed.All(s => s.Document.State == SourceState.Missing) ? SourceMatch.NoEvidence : SourceMatch.Unreadable
            };
        }

        var allNumbers = ContentNumbers(content, derived.Select(x => x.Property).ToList(), out var derivedNumbers);
        var numbers = Sample(allNumbers);
        var percent = Percent(entry.Unit);
        var quote = string.IsNullOrWhiteSpace(entry.SourceQuote) ? null : entry.SourceQuote;
        var sections = readable
            .SelectMany(s => s.Document.Sections.Select(x => new Section(s.Url, s.Document.Name, x.Heading, x.Text, languages)))
            .ToList();

        SourceResult Result(SourceMatch match, List<decimal> found = null, string location = null, string ambiguity = null)
        {
            var tooLong = location is { Length: > Checker.MaxLocationLength };
            return new()
            {
                Key = entry.Key, Urls = urls, Match = match, NumbersTotal = numbers.Count, ContentNumbers = allNumbers.Count,
                NumbersFound = found?.Count ?? 0,
                Missing = found == null ? [] : numbers.Except(found).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(),
                Location = tooLong ? null : location, Reason = reason, Ambiguity = ambiguity, Quote = quote, Derived = derived,
                Zero = zero, NotApplicable = notApplicable,
                Note = tooLong ? LocationTooLongNote
                    : match == SourceMatch.NoNumbers && derivedNumbers > 0 ? AllDerivedNote : null
            };
        }

        if (quote != null)
        {
            var canonicalQuote = Canonical(quote);
            var quoted = sections.Where(s => s.CanonicalText.Contains(canonicalQuote, StringComparison.OrdinalIgnoreCase)).ToList();
            if (quoted.Count == 0)
            {
                return Result(SourceMatch.QuoteNotFound);
            }
            if (numbers.Count == 0)
            {
                return Result(SourceMatch.NoNumbers);
            }
            var quoteNumbers = SourceNumbers.Scan(quote, languages);
            var best = quoted
                .Select(s => (Section: s, Found: numbers.Where(n => s.Numbers.Contains(n, percent) || quoteNumbers.Contains(n, percent)).ToList()))
                .OrderByDescending(x => x.Found.Count)
                .First();
            var anchored = numbers.Where(n => quoteNumbers.Contains(n, percent)).ToList();
            if (anchored.Count == 0)
            {
                // a quote without a number of the entry locates the section that holds the numbers
                var sectionMatch = Classify(best.Found.Count, numbers.Count);
                return Result(sectionMatch, best.Found,
                    sectionMatch == SourceMatch.Found ? Location(sources, [best.Section]) : null);
            }

            // the quote anchors its numbers, the other numbers of the entry are looked up in all sources
            var others = numbers.Except(anchored).ToList();
            var quoteFound = anchored
                .Concat(others.Where(n => sections.Any(s => s.Numbers.Contains(n, percent))))
                .ToList();
            var quoteMatch = Classify(quoteFound.Count, numbers.Count);
            if (quoteMatch != SourceMatch.Found)
            {
                return Result(quoteMatch, quoteFound);
            }
            if (period != null)
            {
                var weakNumbers = QuoteYearWeakNumbers(anchored, quote, quoteNumbers, percent, period.Value, languages)
                    .Concat(YearWeakNumbers(others, sections, percent, period.Value))
                    .ToHashSet();
                if (weakNumbers.Count > 0)
                {
                    return Result(SourceMatch.Ambiguous, quoteFound,
                        ambiguity: MultiYearReason(numbers, numbers.Where(weakNumbers.Contains).ToList()));
                }
            }
            return Result(quoteMatch, quoteFound, CoverLocation(sources, sections, others, percent, best.Section));
        }

        if (numbers.Count == 0)
        {
            return Result(SourceMatch.NoNumbers);
        }
        var found = numbers.Where(n => sections.Any(s => s.Numbers.Contains(n, percent))).ToList();
        var match = Classify(found.Count, numbers.Count);
        if (match != SourceMatch.Found)
        {
            return Result(match, found);
        }

        var weak = Weakness(numbers, sections, percent);
        if (weak == null && period != null)
        {
            var weakNumbers = YearWeakNumbers(numbers, sections, percent, period.Value);
            weak = weakNumbers.Count > 0 ? MultiYearReason(numbers, weakNumbers) : null;
        }
        if (weak != null)
        {
            return Result(SourceMatch.Ambiguous, found, ambiguity: weak);
        }
        return Result(match, found, CoverLocation(sources, sections, found, percent));
    }

    /// <summary>
    /// Source location of the sections that hold the numbers (greedy cover: the sections with most of the numbers,
    /// at most <see cref="MaxSectionsPerSource"/> per source), null when the numbers are not covered
    /// </summary>
    /// <param name="sources">Sources of the entry</param>
    /// <param name="sections">Sections to search</param>
    /// <param name="numbers">Numbers to cover</param>
    /// <param name="percent">Lookup of a value below 1 as percentage</param>
    /// <param name="first">Section that starts the cover (section of the source quote), null for none</param>
    private static string CoverLocation(IReadOnlyList<(string Url, SourceDocument Document)> sources,
        List<Section> sections, IEnumerable<decimal> numbers, PercentLookup percent, Section first = null)
    {
        var remaining = new HashSet<decimal>(numbers);
        var cover = new List<Section>();
        var perSource = new Dictionary<string, int>(StringComparer.Ordinal);
        void AddToCover(Section section)
        {
            cover.Add(section);
            perSource[section.Url ?? string.Empty] = perSource.GetValueOrDefault(section.Url ?? string.Empty) + 1;
            remaining.RemoveWhere(n => section.Numbers.Contains(n, percent));
        }

        if (first != null)
        {
            AddToCover(first);
        }
        while (remaining.Count > 0)
        {
            var best = sections
                .Where(s => perSource.GetValueOrDefault(s.Url ?? string.Empty) < MaxSectionsPerSource)
                .Select(s => (Section: s, Hits: remaining.Count(n => s.Numbers.Contains(n, percent))))
                .OrderByDescending(x => x.Hits).FirstOrDefault();
            if (best.Section == null || best.Hits == 0) break;
            AddToCover(best.Section);
        }
        return remaining.Count == 0 ? Location(sources, cover) : null;
    }

    private static SourceMatch Classify(int found, int total) =>
        found == total ? SourceMatch.Found : found == 0 ? SourceMatch.NotFound : SourceMatch.Partial;

    /// <summary>
    /// Weak evidence of a single number, null for an entry with several numbers or a distinctive number
    /// </summary>
    private static string Weakness(List<decimal> numbers, List<Section> sections, PercentLookup percent)
    {
        if (numbers.Count != 1)
        {
            return null;
        }
        var number = numbers[0];
        var hits = sections.Where(s => s.Numbers.Contains(number, percent)).ToList();
        if (hits.All(s => s.Numbers.IsWordOnly(number, percent)))
        {
            return "number in words";
        }
        var digits = hits.Select(s => s.Numbers.WrittenDigits(number, percent)).Where(x => x != null).Min();
        if (digits <= WeakDigits)
        {
            return "short number";
        }
        return digits <= FrequentDigits && hits.Count > WeakSections ? $"number in {hits.Count} sections" : null;
    }

    /// <summary>
    /// Year-weak numbers: at least one year-weak hit and no positive hit (see <see cref="YearContext"/>)
    /// </summary>
    /// <param name="numbers">Numbers to rate</param>
    /// <param name="sections">Sections to search</param>
    /// <param name="percent">Lookup of a value below 1 as percentage</param>
    /// <param name="period">Relevant period: the reference year of the entry or the year of validFrom</param>
    private static List<decimal> YearWeakNumbers(List<decimal> numbers, List<Section> sections, PercentLookup percent,
        RelevantPeriod period) =>
        numbers
            .Where(n =>
            {
                var contexts = sections
                    .SelectMany(s => s.Numbers.Hits(n, percent).Select(h => s.YearContext(h, period)))
                    .ToList();
                return contexts.Contains(HitYear.Weak) && !contexts.Contains(HitYear.Positive);
            })
            .ToList();

    /// <summary>
    /// Year-weak numbers of a source quote, rated within the quote only: a quote with the relevant year (or a year
    /// group that holds it; for a tax year the period starting in the year) or without year anchors its numbers;
    /// a number of the quote whose closest year is another year is year-weak
    /// </summary>
    /// <param name="numbers">Numbers of the entry within the quote</param>
    /// <param name="quote">Source quote of the entry</param>
    /// <param name="quoteNumbers">Numbers of the quote</param>
    /// <param name="percent">Lookup of a value below 1 as percentage</param>
    /// <param name="period">Relevant period: the reference year of the entry or the year of validFrom</param>
    /// <param name="languages">Languages</param>
    private static List<decimal> QuoteYearWeakNumbers(List<decimal> numbers, string quote, SourceNumbers quoteNumbers,
        PercentLookup percent, RelevantPeriod period, LanguageSet languages)
    {
        var years = YearHits(quote, languages);
        if (years.Any(x => Holds(x, period)))
        {
            return [];
        }
        return numbers
            .Where(n => quoteNumbers.Hits(n, percent).Any(h => YearContext(h, years, period) == HitYear.Weak))
            .ToList();
    }

    /// <summary>
    /// Ambiguity of year-weak numbers; a bundle lists the affected numbers
    /// </summary>
    private static string MultiYearReason(List<decimal> numbers, List<decimal> affected) =>
        numbers.Count == 1
            ? MultiYearAmbiguity
            : $"{MultiYearAmbiguity} ({string.Join(", ", affected.Select(n => n.ToString(CultureInfo.InvariantCulture)))})";

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
        return year == null ? null : new RelevantPeriod(year.Value, IsTaxYearStart(validFrom));
    }

    private static bool IsTaxYearStart(string validFrom) =>
        validFrom is { Length: >= 10 } &&
        DateOnly.TryParseExact(validFrom[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var date) &&
        (date.Month != 1 || date.Day != 1);

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
    internal static HitYear YearContext((int Index, int Length) hit, IEnumerable<YearHit> years, RelevantPeriod period)
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
    private static bool Holds(YearHit candidate, RelevantPeriod period) =>
        period.Fiscal
            ? candidate.PeriodStart == period.Year
            : (candidate.PeriodStart == null || candidate.Plain) && candidate.Years.Contains(period.Year);

    /// <summary>
    /// Rating of the closest year hit (see <see cref="YearContext"/>)
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
    /// <see cref="SourceMatch.Ambiguous"/> (see <see cref="YearContext"/>);
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
            years.Add(new YearHit([value], match.Index, match.Length, validity));
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

    /// <summary>
    /// Source location of the covering sections; with several sources one part per source, in the order of the
    /// sources, named by the evidence file or, without file name, by the host
    /// </summary>
    private static string Location(IReadOnlyList<(string Url, SourceDocument Document)> sources, List<Section> cover)
    {
        if (sources.Count == 1)
        {
            var sections = Sections(cover.Select(x => x.Heading).ToList());
            var name = cover[0].Name;
            return name != null ? $"{name}: {sections}" : sections;
        }
        return string.Join("; ", sources.DistinctBy(s => s.Url, StringComparer.Ordinal)
            .Select(s => (Source: s, Headings: cover.Where(x => string.Equals(x.Url, s.Url, StringComparison.Ordinal))
                .Select(x => x.Heading).ToList()))
            .Where(x => x.Headings.Count > 0)
            .Select(x => $"{Label(x.Source.Url, x.Source.Document)}: {Sections(x.Headings)}"));
    }

    /// <summary>
    /// Source name: the evidence file name of the document, otherwise the host of the URL
    /// </summary>
    private static string Label(string url, SourceDocument document) =>
        string.IsNullOrWhiteSpace(document?.Name) ? Label(url) : document.Name;

    private static string Sections(List<string> headings) =>
        headings.Count == 1
            ? $"Section \"{headings[0]}\""
            : "Sections " + string.Join(", ", headings.Select(h => $"\"{h}\""));

    /// <summary>
    /// Short source name: the host of a URL without www, otherwise the text
    /// </summary>
    private static string Label(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
            : url ?? "source";

    // quote --------------------------------------------------------------------

    private static readonly Regex Hyphenation = new(@"(?<=\p{L})-\s+(?=\p{Ll})", RegexOptions.Compiled);
    private static readonly Regex WordHyphen = new(@"(?<=\p{L})-(?=\p{L})", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Canonical text for the quote comparison: script normalized (see <see cref="SourceScript"/>), typographic
    /// quotes and apostrophes unified, hyphenation at a line break joined (<c>supple- mental</c>), hyphens within
    /// words dropped and whitespace collapsed
    /// </summary>
    internal static string Canonical(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in SourceScript.Normalize(text))
        {
            switch (c)
            {
                case '\u00ad':
                    break;
                case '\u2018' or '\u2019' or '\u201a' or '\u201b' or '\u2032' or '`':
                    builder.Append('\'');
                    break;
                case '\u201c' or '\u201d' or '\u201e' or '\u201f' or '\u2033':
                    builder.Append('"');
                    break;
                case '\u2010' or '\u2011':
                    builder.Append('-');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
        var canonical = Hyphenation.Replace(builder.ToString(), string.Empty);
        canonical = WordHyphen.Replace(canonical, string.Empty);
        return Whitespace.Replace(canonical, " ").Trim();
    }

    private sealed class Section(string url, string name, string heading, string text, LanguageSet languages)
    {
        internal string Url => url;
        internal string Name => name;
        internal string Heading => heading;
        internal SourceNumbers Numbers { get; } = SourceNumbers.Scan(text, languages);
        internal string CanonicalText => field ??= Canonical(text);
        private List<YearHit> TextYears => field ??=
            [.. YearHits(heading, languages).Select(x => x.AtStart()), .. YearHits(text, languages)];

        /// <summary>Year context of a hit in the section text</summary>
        internal HitYear YearContext((int Index, int Length) hit, RelevantPeriod period) =>
            SourceVerifier.YearContext(hit, TextYears, period);
    }

    // manifest -----------------------------------------------------------------

    /// <summary>
    /// Verify the entries of a manifest with source URLs (inline content and JSON content files) against the local
    /// evidence files or, with a live loader, against the source URLs. Per source URL the local evidence is the
    /// documentRef for the first URL, otherwise the declared file with the same URL.
    /// Entries that reference an official calculator are skipped. The languages of the manifest
    /// (<see cref="Manifest.Languages"/>, default: all available languages) read the number and year words.
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <param name="loadLive">Loader of a live source URL (e.g. <see cref="SourceDocument.LoadLive"/>), null for the local evidence</param>
    /// <param name="documents">Loaded documents by URL (live) or local path, shared over several manifests</param>
    /// <param name="languageDirectory">Folder of additional language files (see <see cref="LanguageSet.Get(IEnumerable{string}, string)"/>),
    /// null for the package languages only</param>
    /// <exception cref="ArgumentException">A language of the manifest without language file</exception>
    public static async Task<List<SourceResult>> VerifyManifest(Manifest manifest, string lexDirectory,
        Func<string, Task<SourceDocument>> loadLive = null, IDictionary<string, SourceDocument> documents = null,
        string languageDirectory = null)
    {
        var languages = LanguageSet.Get(manifest.Languages, languageDirectory);
        documents ??= new Dictionary<string, SourceDocument>(StringComparer.Ordinal);
        var exclusions = manifest.Exclusions.Where(x => x.Kind == Exclusion.KindAutoVerification).ToList();
        var keys = DerivedProperties.Keys(manifest);
        var results = new List<SourceResult>();
        foreach (var entry in manifest.Values)
        {
            var urls = entry.Urls.ToList();
            if (urls.Count == 0 || string.IsNullOrEmpty(entry.Key) || Checker.IsReferenceEntry(entry.Key) ||
                (!entry.HasInlineContent && !entry.HasContentFile))
            {
                continue;
            }

            SourceResult result;
            string evidence = null;
            string reason = null;
            var content = entry.HasInlineContent ? entry.Content : ContentFileText(entry, lexDirectory, out reason);
            if (content == null)
            {
                result = new SourceResult
                {
                    Key = entry.Key, Urls = urls, Match = SourceMatch.NoContent, Reason = reason, Languages = languages.Label
                };
            }
            else
            {
                var sources = new List<(string Url, SourceDocument Document)>();
                for (var i = 0; i < urls.Count; i++)
                {
                    var url = urls[i];
                    SourceDocument document;
                    if (loadLive != null)
                    {
                        if (!documents.TryGetValue(url, out document))
                        {
                            documents[url] = document = await loadLive(url);
                        }
                    }
                    else
                    {
                        var localPath = LocalEvidence(manifest, entry, url, i == 0, lexDirectory);
                        if (localPath != null)
                        {
                            evidence ??= Path.GetFileName(localPath);
                        }
                        var cacheKey = localPath ?? string.Empty;
                        if (!documents.TryGetValue(cacheKey, out document))
                        {
                            documents[cacheKey] = document = await SourceDocument.LoadLocal(localPath);
                        }
                    }
                    sources.Add((url, document));
                }
                result = Verify(entry, sources, content, DerivedProperties.Of(entry.Key, manifest.Exclusions, keys),
                    manifest.ValidFrom, languages);
            }
            result.Evidence = evidence;
            result.ExcludedReason = exclusions.FirstOrDefault(x => x.Matches(entry.Key))?.Reason;
            results.Add(result);
        }
        return results;
    }

    private static string ContentFileText(Entry entry, string lexDirectory, out string reason)
    {
        try
        {
            return EntryMapping.Content(entry, lexDirectory, out reason);
        }
        catch (IOException exception)
        {
            reason = exception.Message;
            return null;
        }
    }

    /// <summary>
    /// Local evidence file of a source URL: the documentRef for the main source, otherwise a declared file
    /// with the same source URL
    /// </summary>
    private static string LocalEvidence(Manifest manifest, Entry entry, string url, bool mainSource, string lexDirectory)
    {
        var name = mainSource && !string.IsNullOrWhiteSpace(entry.DocumentRef)
            ? entry.DocumentRef
            : manifest.Files.FirstOrDefault(f => f.Urls.Contains(url, StringComparer.Ordinal))?.FileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        var path = Path.Combine(lexDirectory, name);
        return File.Exists(path) ? path : null;
    }
}
