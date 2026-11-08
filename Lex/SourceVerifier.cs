using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Looks up the numbers of lex entries in their source documents
/// </summary>
public static class SourceVerifier
{
    /// <summary>A single number written with this many digits or less is weak evidence (22%, 1.5, 50)</summary>
    private const int WeakDigits = 2;

    /// <summary>A single number written with this many digits or less that occurs in more than
    /// <see cref="WeakSections"/> sections is weak evidence</summary>
    private const int FrequentDigits = 3;

    /// <summary>A single number that occurs in more sections is weak evidence</summary>
    private const int WeakSections = 3;

    /// <summary>Ambiguity of a year-weak number (see <see cref="YearAnalyzer.Classify"/>)</summary>
    internal const string MultiYearAmbiguity = "multi-year section: add sourceQuote with the year";

    /// <summary>Note of an entry whose numbers are all derived</summary>
    internal const string AllDerivedNote = "all numbers derived: confirm with LexAttest";

    /// <summary>Hint for zero properties of a bundle without a notApplicable exclusion</summary>
    // ReSharper disable once UnusedMember.Global
    public const string UnjustifiedZeroHint = "zero values without justification";

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
    /// the entry, rates a year-weak number as <see cref="SourceMatch.Ambiguous"/> (see <see cref="YearAnalyzer.Classify"/>);
    /// a validFrom not on the 1st of January starts a tax year with this start year (see <see cref="YearAnalyzer.Period"/>),
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
        var context = new EntryContext(entry, sources, content ?? entry.Content, exclusions, validFrom, languages);
        if (context.Sections == null)
        {
            return context.Unreadable();
        }
        return context.Quote != null ? MatchQuote(context) : MatchNumbers(context);
    }

    /// <summary>
    /// Match of an entry without source quote: all numbers in the sections, rated for weak evidence
    /// </summary>
    private static SourceResult MatchNumbers(EntryContext context)
    {
        var numbers = context.Numbers;
        if (numbers.Count == 0)
        {
            return context.Result(SourceMatch.NoNumbers);
        }
        var found = numbers.Where(context.InSections).ToList();
        var match = Classify(found.Count, numbers.Count);
        if (match != SourceMatch.Found)
        {
            return context.Result(match, found);
        }

        var weak = Weakness(numbers, context.Sections, context.Percent);
        if (weak == null && context.Period != null)
        {
            var weakNumbers = YearWeakNumbers(numbers, context.Sections, context.Percent, context.Period.Value);
            weak = weakNumbers.Count > 0 ? MultiYearReason(numbers, weakNumbers) : null;
        }
        if (weak != null)
        {
            return context.Result(SourceMatch.Ambiguous, found, ambiguity: weak);
        }
        return context.Result(match, found, SourceLocation.CoverLocation(context.Sources, context.Sections, found, context.Percent));
    }

    /// <summary>
    /// Match of an entry with source quote: the quote must be in one section and locates the numbers
    /// </summary>
    private static SourceResult MatchQuote(EntryContext context)
    {
        var canonicalQuote = SourceLocation.Canonical(context.Quote);
        var quoted = context.Sections
            .Where(s => s.CanonicalText.Contains(canonicalQuote, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (quoted.Count == 0)
        {
            return context.Result(SourceMatch.QuoteNotFound);
        }
        if (context.Numbers.Count == 0)
        {
            return context.Result(SourceMatch.NoNumbers);
        }
        var quoteNumbers = SourceNumbers.Scan(context.Quote, context.Languages);
        var best = quoted
            .Select(s => (Section: s, Found: context.Numbers
                .Where(n => s.Numbers.Contains(n, context.Percent) || quoteNumbers.Contains(n, context.Percent)).ToList()))
            .OrderByDescending(x => x.Found.Count)
            .First();
        var anchored = context.Numbers.Where(n => quoteNumbers.Contains(n, context.Percent)).ToList();
        if (anchored.Count == 0)
        {
            // a quote without a number of the entry locates the section that holds the numbers
            var sectionMatch = Classify(best.Found.Count, context.Numbers.Count);
            return context.Result(sectionMatch, best.Found,
                sectionMatch == SourceMatch.Found ? SourceLocation.Location(context.Sources, [best.Section]) : null);
        }
        return MatchAnchored(context, anchored, quoteNumbers, best.Section);
    }

    /// <summary>
    /// Match of a quote that anchors numbers of the entry; the other numbers are looked up in all sources
    /// </summary>
    private static SourceResult MatchAnchored(EntryContext context, List<decimal> anchored, SourceNumbers quoteNumbers,
        SourceSection quoteSection)
    {
        var numbers = context.Numbers;
        var others = numbers.Except(anchored).ToList();
        var found = anchored.Concat(others.Where(context.InSections)).ToList();
        var match = Classify(found.Count, numbers.Count);
        if (match != SourceMatch.Found)
        {
            return context.Result(match, found);
        }
        if (context.Period != null)
        {
            var weakNumbers = QuoteYearWeakNumbers(anchored, context.Quote, quoteNumbers, context.Percent,
                    context.Period.Value, context.Languages)
                .Concat(YearWeakNumbers(others, context.Sections, context.Percent, context.Period.Value))
                .ToHashSet();
            if (weakNumbers.Count > 0)
            {
                return context.Result(SourceMatch.Ambiguous, found,
                    ambiguity: MultiYearReason(numbers, numbers.Where(weakNumbers.Contains).ToList()));
            }
        }
        return context.Result(match, found,
            SourceLocation.CoverLocation(context.Sources, context.Sections, others, context.Percent, quoteSection));
    }

    /// <summary>
    /// Input of the verification of one entry: numbers, readable sections and the result builder
    /// </summary>
    private sealed class EntryContext
    {
        private readonly Entry entry;
        private readonly List<string> urls;
        private readonly string reason;
        private readonly List<DerivedValue> derived;
        private readonly List<DerivedValue> notApplicable;
        private readonly List<string> zero;
        private readonly List<decimal> allNumbers;
        private readonly int derivedNumbers;
        private readonly bool missing;

        internal EntryContext(Entry entry, IReadOnlyList<(string Url, SourceDocument Document)> sources, string content,
            IReadOnlyList<Exclusion> exclusions, string validFrom, LanguageSet languages)
        {
            this.entry = entry;
            Sources = sources;
            Languages = languages;
            Period = YearAnalyzer.Period(entry.ReferenceYear, validFrom);
            urls = entry.Urls.ToList();
            derived = DerivedProperties.Values(entry.Key, content, exclusions);
            notApplicable = DerivedProperties.Values(entry.Key, content, exclusions, Exclusion.KindNotApplicable);
            zero = DerivedProperties.Zeros(content);

            var readable = sources.Where(s => s.Document.State == SourceState.Readable).ToList();
            var failed = sources.Where(s => s.Document.State != SourceState.Readable).ToList();
            reason = sources.Count == 1
                ? failed.FirstOrDefault().Document?.Reason
                : failed.Count == 0 ? null
                : string.Join("; ", failed.Select(s => $"{SourceLocation.Label(s.Url, s.Document)}: {s.Document.Reason}"));
            if (readable.Count == 0)
            {
                missing = failed.All(s => s.Document.State == SourceState.Missing);
                return;
            }

            allNumbers = EntryNumbers.ContentNumbers(content, derived.Select(x => x.Property).ToList(), out derivedNumbers);
            Numbers = EntryNumbers.Sample(allNumbers);
            Percent = UnitKind.Percent(entry.Unit);
            Quote = string.IsNullOrWhiteSpace(entry.SourceQuote) ? null : entry.SourceQuote;
            Sections = readable
                .SelectMany(s => s.Document.Sections.Select(x => new SourceSection(s.Url, s.Document.Name, x.Heading, x.Text, languages)))
                .ToList();
        }

        internal IReadOnlyList<(string Url, SourceDocument Document)> Sources { get; }
        internal LanguageSet Languages { get; }
        internal RelevantPeriod? Period { get; }
        internal List<decimal> Numbers { get; }
        internal PercentLookup Percent { get; }
        internal string Quote { get; }

        /// <summary>Sections of the readable sources, null without readable source</summary>
        internal List<SourceSection> Sections { get; }

        internal bool InSections(decimal number) => Sections.Any(s => s.Numbers.Contains(number, Percent));

        internal SourceResult Unreadable() => new()
        {
            Key = entry.Key, Urls = urls, Reason = reason, Derived = derived, Zero = zero, NotApplicable = notApplicable,
            Match = missing ? SourceMatch.NoEvidence : SourceMatch.Unreadable
        };

        internal SourceResult Result(SourceMatch match, List<decimal> found = null, string location = null, string ambiguity = null)
        {
            var tooLong = location is { Length: > TraceabilityCheck.MaxLocationLength };
            return new()
            {
                Key = entry.Key, Urls = urls, Match = match, NumbersTotal = Numbers.Count, ContentNumbers = allNumbers.Count,
                NumbersFound = found?.Count ?? 0,
                Missing = found == null ? [] : Numbers.Except(found).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(),
                Location = tooLong ? null : location, Reason = reason, Ambiguity = ambiguity, Quote = Quote, Derived = derived,
                Zero = zero, NotApplicable = notApplicable,
                Note = tooLong ? SourceLocation.LocationTooLongNote
                    : match == SourceMatch.NoNumbers && derivedNumbers > 0 ? AllDerivedNote : null
            };
        }
    }


    private static SourceMatch Classify(int found, int total) =>
        found == total ? SourceMatch.Found : found == 0 ? SourceMatch.NotFound : SourceMatch.Partial;

    /// <summary>
    /// Weak evidence of a single number, null for an entry with several numbers or a distinctive number
    /// </summary>
    private static string Weakness(List<decimal> numbers, List<SourceSection> sections, PercentLookup percent)
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
    /// Year-weak numbers: at least one year-weak hit and no positive hit (see <see cref="YearAnalyzer.Classify"/>)
    /// </summary>
    /// <param name="numbers">Numbers to rate</param>
    /// <param name="sections">Sections to search</param>
    /// <param name="percent">Lookup of a value below 1 as percentage</param>
    /// <param name="period">Relevant period: the reference year of the entry or the year of validFrom</param>
    private static List<decimal> YearWeakNumbers(List<decimal> numbers, List<SourceSection> sections, PercentLookup percent,
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
        var years = YearAnalyzer.YearHits(quote, languages);
        if (years.Any(x => YearAnalyzer.Holds(x, period)))
        {
            return [];
        }
        return numbers
            .Where(n => quoteNumbers.Hits(n, percent).Any(h => YearAnalyzer.Classify(h, years, period) == HitYear.Weak))
            .ToList();
    }

    /// <summary>
    /// Ambiguity of year-weak numbers; a bundle lists the affected numbers
    /// </summary>
    private static string MultiYearReason(List<decimal> numbers, List<decimal> affected) =>
        numbers.Count == 1
            ? MultiYearAmbiguity
            : $"{MultiYearAmbiguity} ({string.Join(", ", affected.Select(n => n.ToString(CultureInfo.InvariantCulture)))})";
}
