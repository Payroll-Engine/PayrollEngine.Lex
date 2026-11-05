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

    /// <summary>All numbers are found, but the evidence is weak: the entry holds a single number that is short
    /// (two significant digits at most), occurs in many sections or is found in words only.
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

    /// <summary>The source location and the automatic verification record may be written</summary>
    public bool CanWrite => Match == SourceMatch.Found && ExcludedReason == null;
}

/// <summary>
/// Looks up the numbers of lex entries in their source documents
/// </summary>
public static class SourceVerifier
{
    private const int MaxNumbers = 40;
    private const int MaxSections = 3;

    /// <summary>A single number with this many significant digits or less is weak evidence</summary>
    private const int WeakDigits = 2;

    /// <summary>A single number that occurs in more sections is weak evidence</summary>
    private const int WeakSections = 3;

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
    public static Verification AutoVerification(DateOnly verifiedOn, string evidence) => new()
    {
        Mode = Verification.ModeAuto,
        VerifiedOn = verifiedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Method = Method,
        Evidence = evidence
    };

    /// <summary>
    /// Numbers of an entry content (scalars, object properties, array items), sorted; 0, 1 and open upper bounds
    /// (nines only, see <see cref="IsPlaceholder"/>) carry no information
    /// </summary>
    internal static List<decimal> ContentNumbers(string content)
    {
        var numbers = new HashSet<decimal>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(content);
            Collect(document.RootElement, numbers);
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

    private static void Collect(JsonElement element, HashSet<decimal> numbers)
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
                foreach (var property in element.EnumerateObject()) Collect(property.Value, numbers);
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

    /// <summary>
    /// Significant digits of a number: 0.5, 22 and 340 have two at most, 7.25 has three
    /// </summary>
    internal static int SignificantDigits(decimal value) =>
        Math.Abs(value).ToString(CultureInfo.InvariantCulture).Replace(".", string.Empty).Trim('0').Length;

    private static readonly string[] RateTokens = ["%", "percent", "rate", "fraction"];

    private static readonly Regex UnitToken = new("%|[a-z]+", RegexOptions.Compiled);

    /// <summary>
    /// The unit names a rate (<c>%</c>, <c>percent</c>, <c>rate</c> or <c>fraction</c>, also within a combined
    /// unit like <c>USD / rate</c>): a value below 1 is also looked up as percentage
    /// </summary>
    internal static bool IsRateUnit(string unit) =>
        !string.IsNullOrWhiteSpace(unit) &&
        UnitToken.Matches(unit.ToLowerInvariant()).Any(x => RateTokens.Contains(x.Value));

    // a location written by LexVerifySource: Section "…" or Sections "…", "…", optionally with a source prefix
    private static readonly Regex ToolLocation = new(
        @"^(?:[^"":;]+: )?Sections? ""[^""]*""(?:, ""[^""]*"")*(?:; [^"":;]+: Sections? ""[^""]*""(?:, ""[^""]*"")*)*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Test if a source location was written by LexVerifySource (and may be refreshed):
    /// <c>Section "Page 24"</c>, <c>Sections "Page 1", "Page 2"</c>, optionally prefixed with the evidence file
    /// or the host (<c>p15.pdf: Section "Page 24"</c>, several sources separated by <c>; </c>)
    /// </summary>
    public static bool IsToolLocation(string location) =>
        !string.IsNullOrWhiteSpace(location) && ToolLocation.IsMatch(location.Trim());

    /// <summary>
    /// Verify one entry against the document of its single source
    /// </summary>
    public static SourceResult Verify(Entry entry, SourceDocument document) =>
        Verify(entry, [(entry.Urls.FirstOrDefault(), document)]);

    /// <summary>
    /// Verify one entry against the documents of its sources: a number is found when any readable
    /// document holds it. With a source quote, the numbers must be in the section that holds the quote.
    /// </summary>
    /// <param name="entry">Lex entry</param>
    /// <param name="sources">Source URL and document, in the order of the entry source URLs</param>
    /// <param name="content">Content to verify, default: the inline content of the entry (the text of a content file)</param>
    public static SourceResult Verify(Entry entry, IReadOnlyList<(string Url, SourceDocument Document)> sources,
        string content = null)
    {
        var urls = entry.Urls.ToList();
        var readable = sources.Where(s => s.Document.State == SourceState.Readable).ToList();
        var failed = sources.Where(s => s.Document.State != SourceState.Readable).ToList();
        var reason = sources.Count == 1
            ? failed.FirstOrDefault().Document?.Reason
            : failed.Count == 0 ? null
            : string.Join("; ", failed.Select(s => $"{Label(s.Url)}: {s.Document.Reason}"));
        if (readable.Count == 0)
        {
            return new SourceResult
            {
                Key = entry.Key, Urls = urls, Reason = reason,
                Match = failed.All(s => s.Document.State == SourceState.Missing) ? SourceMatch.NoEvidence : SourceMatch.Unreadable
            };
        }

        var allNumbers = ContentNumbers(content ?? entry.Content);
        var numbers = Sample(allNumbers);
        var rate = IsRateUnit(entry.Unit);
        var quote = string.IsNullOrWhiteSpace(entry.SourceQuote) ? null : entry.SourceQuote;
        var sections = readable
            .SelectMany(s => s.Document.Sections.Select(x => new Section(s.Url, s.Document.Name, x.Heading, x.Text)))
            .ToList();

        SourceResult Result(SourceMatch match, List<decimal> found = null, string location = null, string ambiguity = null) => new()
        {
            Key = entry.Key, Urls = urls, Match = match, NumbersTotal = numbers.Count, ContentNumbers = allNumbers.Count,
            NumbersFound = found?.Count ?? 0,
            Missing = found == null ? [] : numbers.Except(found).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(),
            Location = location, Reason = reason, Ambiguity = ambiguity, Quote = quote
        };

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
                .Select(s => (Section: s, Found: numbers.Where(n => s.Numbers.Contains(n, rate) || quoteNumbers.Contains(n, rate)).ToList()))
                .OrderByDescending(x => x.Found.Count)
                .First();
            var quoteMatch = Classify(best.Found.Count, numbers.Count);
            return Result(quoteMatch, best.Found,
                quoteMatch == SourceMatch.Found ? Location(sources.Count, [best.Section]) : null);
        }

        if (numbers.Count == 0)
        {
            return Result(SourceMatch.NoNumbers);
        }
        var found = numbers.Where(n => sections.Any(s => s.Numbers.Contains(n, rate))).ToList();
        var match = Classify(found.Count, numbers.Count);
        if (match != SourceMatch.Found)
        {
            return Result(match, found);
        }

        var weak = Weakness(numbers, sections, rate);
        if (weak != null)
        {
            return Result(SourceMatch.Ambiguous, found, ambiguity: weak);
        }

        // greedy cover: the sections that hold most of the numbers
        var remaining = new HashSet<decimal>(found);
        var cover = new List<Section>();
        while (remaining.Count > 0 && cover.Count <= MaxSections)
        {
            var best = sections
                .Select(s => (Section: s, Hits: remaining.Where(n => s.Numbers.Contains(n, rate)).ToList()))
                .OrderByDescending(x => x.Hits.Count).First();
            if (best.Hits.Count == 0) break;
            cover.Add(best.Section);
            remaining.ExceptWith(best.Hits);
        }
        var location = remaining.Count == 0 && cover.Count <= MaxSections ? Location(sources.Count, cover) : null;
        return Result(match, found, location);
    }

    private static SourceMatch Classify(int found, int total) =>
        found == total ? SourceMatch.Found : found == 0 ? SourceMatch.NotFound : SourceMatch.Partial;

    /// <summary>
    /// Weak evidence of a single number, null for an entry with several numbers or a distinctive number
    /// </summary>
    private static string Weakness(List<decimal> numbers, List<Section> sections, bool rate)
    {
        if (numbers.Count != 1)
        {
            return null;
        }
        var number = numbers[0];
        var hits = sections.Where(s => s.Numbers.Contains(number, rate)).ToList();
        if (hits.All(s => s.Numbers.IsWordOnly(number, rate)))
        {
            return "number in words";
        }
        if (SignificantDigits(number) <= WeakDigits)
        {
            return "short number";
        }
        return hits.Count > WeakSections ? $"number in {hits.Count} sections" : null;
    }

    private static string Location(int sourceCount, List<Section> cover)
    {
        if (sourceCount == 1)
        {
            var sections = Sections(cover.Select(x => x.Heading).ToList());
            var name = cover[0].Name;
            return name != null ? $"{name}: {sections}" : sections;
        }
        return string.Join("; ", cover.GroupBy(x => x.Url)
            .Select(g => $"{Label(g.Key)}: {Sections(g.Select(x => x.Heading).ToList())}"));
    }

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
                result = Verify(entry, sources, content);
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
