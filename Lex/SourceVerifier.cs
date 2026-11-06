using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
/// Year or year group (enumeration, range) of a text with its position
/// </summary>
internal sealed class YearHit(List<int> years, int index, int length, bool validity = false)
{
    /// <summary>Years of the hit, one year or the years of a group</summary>
    internal List<int> Years => years;

    /// <summary>Text position of the first year</summary>
    internal int Index => index;

    /// <summary>Text length up to the end of the last year</summary>
    internal int Length => length;

    /// <summary>Start of a validity (<c>effective January 1, 2025</c>, <c>2025 and after</c>)</summary>
    internal bool Validity => validity;

    /// <summary>Group with a further year that ends at the text position</summary>
    internal YearHit Join(int year, int end) => new([.. years, year], index, end - index, validity);
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
    public static Verification AutoVerification(DateOnly verifiedOn, string evidence, IEnumerable<string> derived = null)
    {
        var properties = derived?.ToList();
        return new()
        {
            Mode = Verification.ModeAuto,
            VerifiedOn = verifiedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Method = Method,
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

    /// <summary>
    /// A value below 1 may be looked up as percentage (0.085 as 8.5%, the source writes a percent sign):
    /// always, except for units that are no rate — <c>factor</c>, <c>hours</c>, <c>days</c> or a currency code
    /// (<c>USD</c>, <c>EUR</c>); a combined unit like <c>USD / rate</c> allows it
    /// </summary>
    internal static bool AllowsPercent(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return true;
        }
        var tokens = UnitToken.Matches(unit).Select(x => x.Value).ToList();
        return tokens.Count == 0 || !tokens.All(t =>
            CurrencyCode.IsMatch(t) || NonRateTokens.Contains(t, StringComparer.OrdinalIgnoreCase));
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
    public static SourceResult Verify(Entry entry, SourceDocument document, string validFrom = null) =>
        Verify(entry, [(entry.Urls.FirstOrDefault(), document)], validFrom: validFrom);

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
    /// the entry, rates a year-weak number as <see cref="SourceMatch.Ambiguous"/> (see <see cref="YearContext"/>),
    /// null without year context</param>
    public static SourceResult Verify(Entry entry, IReadOnlyList<(string Url, SourceDocument Document)> sources,
        string content = null, IReadOnlyList<Exclusion> exclusions = null, string validFrom = null)
    {
        var year = entry.ReferenceYear ?? ValidYear(validFrom);
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
        var percent = AllowsPercent(entry.Unit);
        var quote = string.IsNullOrWhiteSpace(entry.SourceQuote) ? null : entry.SourceQuote;
        var sections = readable
            .SelectMany(s => s.Document.Sections.Select(x => new Section(s.Url, s.Document.Name, x.Heading, x.Text)))
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
            var quoteNumbers = SourceNumbers.Scan(quote);
            var best = quoted
                .Select(s => (Section: s, Found: numbers.Where(n => s.Numbers.Contains(n, percent) || quoteNumbers.Contains(n, percent)).ToList()))
                .OrderByDescending(x => x.Found.Count)
                .First();
            var quoteMatch = Classify(best.Found.Count, numbers.Count);
            if (quoteMatch == SourceMatch.Found && year != null)
            {
                var quoteWeak = QuoteYearWeakness(numbers, quote, quoteNumbers, percent, year.Value);
                if (quoteWeak != null)
                {
                    return Result(SourceMatch.Ambiguous, best.Found, ambiguity: quoteWeak);
                }
            }
            return Result(quoteMatch, best.Found,
                quoteMatch == SourceMatch.Found ? Location(sources, [best.Section]) : null);
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

        var weak = Weakness(numbers, sections, percent) ?? YearWeakness(numbers, sections, percent, year);
        if (weak != null)
        {
            return Result(SourceMatch.Ambiguous, found, ambiguity: weak);
        }

        // greedy cover: the sections that hold most of the numbers, at most MaxSectionsPerSource per source
        var remaining = new HashSet<decimal>(found);
        var cover = new List<Section>();
        var perSource = new Dictionary<string, int>(StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var best = sections
                .Where(s => perSource.GetValueOrDefault(s.Url ?? string.Empty) < MaxSectionsPerSource)
                .Select(s => (Section: s, Hits: remaining.Where(n => s.Numbers.Contains(n, percent)).ToList()))
                .OrderByDescending(x => x.Hits.Count).FirstOrDefault();
            if (best.Section == null || best.Hits.Count == 0) break;
            cover.Add(best.Section);
            perSource[best.Section.Url ?? string.Empty] = perSource.GetValueOrDefault(best.Section.Url ?? string.Empty) + 1;
            remaining.ExceptWith(best.Hits);
        }
        var location = remaining.Count == 0 ? Location(sources, cover) : null;
        return Result(match, found, location);
    }

    private static SourceMatch Classify(int found, int total) =>
        found == total ? SourceMatch.Found : found == 0 ? SourceMatch.NotFound : SourceMatch.Partial;

    /// <summary>
    /// Weak evidence of a single number, null for an entry with several numbers or a distinctive number
    /// </summary>
    private static string Weakness(List<decimal> numbers, List<Section> sections, bool percent)
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
    /// Weak year context: numbers with at least one year-weak hit and no positive hit (see <see cref="YearContext"/>),
    /// null without year or when no number is year-weak
    /// </summary>
    /// <param name="numbers">Numbers of the entry</param>
    /// <param name="sections">Sections to search</param>
    /// <param name="percent">A value below 1 may be written as percentage</param>
    /// <param name="year">Relevant year: the reference year of the entry or the year of validFrom</param>
    private static string YearWeakness(List<decimal> numbers, List<Section> sections, bool percent, int? year)
    {
        if (year == null)
        {
            return null;
        }
        var affected = numbers
            .Where(n =>
            {
                var contexts = sections
                    .SelectMany(s => s.Numbers.Hits(n, percent).Select(h => s.YearContext(h, year.Value)))
                    .ToList();
                return contexts.Contains(HitYear.Weak) && !contexts.Contains(HitYear.Positive);
            })
            .ToList();
        return affected.Count == 0 ? null : MultiYearReason(numbers, affected);
    }

    /// <summary>
    /// Weak year context of a source quote, rated within the quote only: a quote with the relevant year (or a year
    /// group that holds it) or without year anchors the value; a number of the quote whose closest year is another
    /// year is year-weak
    /// </summary>
    /// <param name="numbers">Numbers of the entry</param>
    /// <param name="quote">Source quote of the entry</param>
    /// <param name="quoteNumbers">Numbers of the quote</param>
    /// <param name="percent">A value below 1 may be written as percentage</param>
    /// <param name="year">Relevant year: the reference year of the entry or the year of validFrom</param>
    private static string QuoteYearWeakness(List<decimal> numbers, string quote, SourceNumbers quoteNumbers, bool percent, int year)
    {
        var years = YearHits(quote);
        if (years.Any(x => x.Years.Contains(year)))
        {
            return null;
        }
        var affected = numbers
            .Where(n => quoteNumbers.Hits(n, percent).Any(h => YearContext(h, years, year) == HitYear.Weak))
            .ToList();
        return affected.Count == 0 ? null : MultiYearReason(numbers, affected);
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

    // four digits 1990–2100, not part of a longer number (12,025, 2025.5), an amount ($2000) or a percentage
    private static readonly Regex YearToken = new(
        @"(?<![\d.,$€£¥])(?<![$€£¥]\s)(?:199\d|20\d\d|2100)(?!\d|[.,]\d|\s?%)", RegexOptions.Compiled);

    // a number row (wage bracket table: 1965 1995 32.50): another number on both sides, no word, label colon
    // or parenthesis in between (2025 (2024: 1.2%) keeps 2024)
    private const int RowWindow = 12;
    private static readonly Regex RowNumber = new(@"\d(?:[\d.,]*\d)?", RegexOptions.Compiled);
    private static readonly Regex RowGap = new(@"^[^\p{L}:()]*$", RegexOptions.Compiled);

    // a validity start: effective January 1, 2025; beginning on or after January 1, 2025; 2025 and after
    private const int ValidityWindow = 64;
    private static readonly Regex ValidityBefore = new(
        @"\b(?:effective(?:\s+(?:on|from|as\s+of))?|beginning(?:\s+(?:on\s+or\s+after|on|after|in|with))?)\s+" +
        @"(?:[a-z]{3,9}\.?\s+\d{1,2}(?:st|nd|rd|th)?,?\s+|\d{1,2}\s+[a-z]{3,9}\.?,?\s+|\d{1,2}[/.\-]\d{1,2}[/.\-])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ValidityAfter = new(@"^\s+and\s+(?:after|later|thereafter|subsequent)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    // a document number: the year followed by a hyphen and up to three digits (2024-40)
    private static readonly Regex DocumentNumberSuffix = new(@"^[-\u2010\u2011\u2013]\d{1,3}(?!\d)", RegexOptions.Compiled);

    // a statute reference before a year in parentheses: Bill 951 (2022), L. 15-97 (2017), Act 32 (2020), § 12 (2017)
    private static readonly Regex StatuteBeforeParenthesis = new(
        @"(?:\b[A-Z][A-Z.]{0,5}|\bAct|\bLaw|\bBill|\bChapter|§)\s?\d[\d.\-]*[A-Za-z]?\s?\($", RegexOptions.Compiled);

    private const int ReferenceWindow = 32;

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
    /// relevant year (<c>effective January 1, 2025</c>, <c>2025 and after</c>): the value may still apply</item>
    /// </list>
    /// A number is year-weak when at least one hit is weak and no hit is positive.
    /// </summary>
    /// <param name="hit">Text position of the hit</param>
    /// <param name="years">Years and year groups of the text with their position (<see cref="YearHits"/>)</param>
    /// <param name="year">Relevant year: the reference year of the entry or the year of validFrom</param>
    internal static HitYear YearContext((int Index, int Length) hit, IEnumerable<YearHit> years, int year)
    {
        YearHit closest = null;
        var distance = int.MaxValue;
        foreach (var candidate in years)
        {
            if (!candidate.Years.Any(y => y >= year - YearsBefore && y <= year + YearsAfter))
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
            if (gap < distance || (gap == distance && candidate.Years.Contains(year)))
            {
                distance = gap;
                closest = candidate;
            }
        }
        if (closest == null || (closest.Validity && closest.Years.Max() <= year))
        {
            return HitYear.Neutral;
        }
        return closest.Years.Contains(year) ? HitYear.Positive : HitYear.Weak;
    }

    // connector of a year enumeration (2024, 2025, or 2026; 2024 and 2025) or a year range (2024-2026, 2024 through 2026)
    private static readonly Regex YearEnumeration = new(@"^(?:\s*,\s*(?:(?:and|or)\s+)?|\s+(?:and|or)\s+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex YearRange = new(@"^(?:\s*[-\u2010-\u2015]\s*|\s+through\s+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const int MaxRangeYears = 10;

    /// <summary>
    /// Years of a text, used for the year context of a number hit and a source quote. A year is a four-digit
    /// number from 1990 to 2100 that is not part of a longer number, an amount or a percentage. Years that
    /// are part of a legal source reference are ignored, because they name the document, not the year of a value:
    /// <list type="bullet">
    /// <item>directly after <c>Notice</c>, <c>Pub.</c> or <c>Rev.</c>, also with a word (<c>Rev. Guide 2024</c>)
    /// or a revision with month (<c>Rev. 12-2025</c>, <c>Rev. December 2025</c>)</item>
    /// <item>in a document number with hyphen (<c>2024-40</c>); this also skips ISO dates and short year
    /// ranges (<c>2025-01-01</c>, <c>2025-26</c>), a range of full years (<c>2024-2025</c>) is kept</item>
    /// <item>in parentheses after a statute reference (<c>Bill 951 (2022)</c>, <c>Act 32 (2020)</c>)</item>
    /// <item>in a print or typesetting mark, a date with a time (<c>15:14 - 3-Dec-2025</c>,
    /// <c>16:58 Oct 31, 2025</c>, <c>Sep 11 2014 16:58</c>): a document is printed before the year it is valid for</item>
    /// <item>in a number row, another number within 12 characters on both sides without a word, colon or
    /// parenthesis in between
    /// (wage bracket table <c>1965 1995 32.50</c>); years of an enumeration or range are no such number</item>
    /// </list>
    /// A validity start (<c>effective January 1, 2025</c>, <c>beginning on or after January 1, 2025</c>,
    /// <c>2025 and after</c>) is kept but marked: up to the relevant year it rates a hit neutral.
    /// A year enumeration (<c>2024, 2025, or 2026</c>, <c>2024 and 2025</c>) and a year range (<c>2024-2026</c>,
    /// <c>2024 through 2026</c>) are one year group. A number with a year-weak hit and no positive hit is
    /// <see cref="SourceMatch.Ambiguous"/> (see <see cref="YearContext"/>); a source quote with the relevant year
    /// anchors the value.
    /// </summary>
    internal static HashSet<int> Years(string text) =>
        YearHits(text).SelectMany(x => x.Years).ToHashSet();

    /// <summary>
    /// Years of a text with their position, without the years of legal source references (see <see cref="Years"/>);
    /// an enumeration or a range of years is one group with all its years
    /// </summary>
    internal static List<YearHit> YearHits(string text)
    {
        var years = new List<YearHit>();
        if (string.IsNullOrEmpty(text))
        {
            return years;
        }
        foreach (Match match in YearToken.Matches(text))
        {
            var start = Math.Max(0, match.Index - ReferenceWindow);
            var before = text[start..match.Index];
            var end = match.Index + match.Length;
            var after = text[end..Math.Min(text.Length, end + 8)];
            if (ReferencePrefix.IsMatch(before) || DocumentNumberSuffix.IsMatch(after) ||
                TimeStampBefore.IsMatch(before) || TimeStampAfter.IsMatch(after) ||
                (after.StartsWith(')') && StatuteBeforeParenthesis.IsMatch(before)) ||
                InNumberRow(text, match.Index, end))
            {
                continue;
            }
            var validity = ValidityBefore.IsMatch(text[Math.Max(0, match.Index - ValidityWindow)..match.Index]) ||
                           ValidityAfter.IsMatch(text[end..Math.Min(text.Length, end + 24)]);
            var value = int.Parse(match.Value, CultureInfo.InvariantCulture);
            var previous = years.Count > 0 ? years[^1] : null;
            var connector = previous != null ? text[(previous.Index + previous.Length)..match.Index] : null;
            if (connector != null && YearEnumeration.IsMatch(connector))
            {
                years[^1] = previous.Join(value, match.Index + match.Length);
                continue;
            }
            if (connector != null && YearRange.IsMatch(connector) && value > previous.Years[^1] &&
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
        return years;
    }

    /// <summary>
    /// A year candidate within a number row: another number within <see cref="RowWindow"/> characters on both
    /// sides without a word, colon or parenthesis in between; a year of an enumeration or range
    /// (<c>2024, 2025, 2026</c>) does not count
    /// </summary>
    private static bool InNumberRow(string text, int index, int end) =>
        RowNeighbor(text[Math.Max(0, index - RowWindow)..index], before: true) &&
        RowNeighbor(text[end..Math.Min(text.Length, end + RowWindow)], before: false);

    private static bool RowNeighbor(string window, bool before)
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
        return !(groupYear && (YearEnumeration.IsMatch(gap) || YearRange.IsMatch(gap)));
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
    /// Canonical text for the quote comparison: typographic quotes and apostrophes unified, hyphenation at
    /// a line break joined (<c>supple- mental</c>), hyphens within words dropped and whitespace collapsed
    /// </summary>
    internal static string Canonical(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
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

    private sealed class Section(string url, string name, string heading, string text)
    {
        internal string Url => url;
        internal string Name => name;
        internal string Heading => heading;
        internal SourceNumbers Numbers { get; } = SourceNumbers.Scan(text);
        internal string CanonicalText => field ??= Canonical(text);
        private List<YearHit> TextYears => field ??= YearHits(text);

        /// <summary>Year context of a hit in the section text</summary>
        internal HitYear YearContext((int Index, int Length) hit, int year) => SourceVerifier.YearContext(hit, TextYears, year);
    }

    // manifest -----------------------------------------------------------------

    /// <summary>
    /// Verify the entries of a manifest with source URLs (inline content and JSON content files) against the local
    /// evidence files or, with a live loader, against the source URLs. Per source URL the local evidence is the
    /// documentRef for the first URL, otherwise the declared file with the same URL.
    /// Entries that reference an official calculator are skipped.
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <param name="loadLive">Loader of a live source URL (e.g. <see cref="SourceDocument.LoadLive"/>), null for the local evidence</param>
    /// <param name="documents">Loaded documents by URL (live) or local path, shared over several manifests</param>
    public static async Task<List<SourceResult>> VerifyManifest(Manifest manifest, string lexDirectory,
        Func<string, Task<SourceDocument>> loadLive = null, IDictionary<string, SourceDocument> documents = null)
    {
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
                result = new SourceResult { Key = entry.Key, Urls = urls, Match = SourceMatch.NoContent, Reason = reason };
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
                    manifest.ValidFrom);
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
