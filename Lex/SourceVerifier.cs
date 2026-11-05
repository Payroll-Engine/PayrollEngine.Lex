using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Verification state of one lex entry against its source
/// </summary>
public enum SourceMatch
{
    /// <summary>All numbers of the entry are found in the source</summary>
    Found,

    /// <summary>Some numbers are found, others are not</summary>
    Partial,

    /// <summary>None of the numbers is found</summary>
    NotFound,

    /// <summary>No source can be read (blocked host, script-only page, scanned PDF)</summary>
    Unreadable,

    /// <summary>The entry holds no numbers to look for</summary>
    NoNumbers,

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

    /// <summary>Numbers of the entry</summary>
    public int NumbersTotal { get; internal init; }

    /// <summary>Numbers found in the source</summary>
    public int NumbersFound { get; internal init; }

    /// <summary>Numbers missing in the source</summary>
    public List<string> Missing { get; internal init; } = [];

    /// <summary>Proposed source location (sections that hold the numbers)</summary>
    public string Location { get; internal init; }

    /// <summary>Reason of an unreadable source; with several sources, the unreadable ones</summary>
    public string Reason { get; internal init; }
}

/// <summary>
/// Looks up the numbers of lex entries in their source documents
/// </summary>
public static class SourceVerifier
{
    private const int MaxNumbers = 40;
    private const int MaxSections = 3;

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
    /// Numbers of an entry content (scalars, object properties, array items); 0, 1 and open upper bounds
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
        return numbers.OrderBy(x => x).Take(MaxNumbers).ToList();
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
    /// Number matches when the source holds it, as a rate (0.085 as 8.5 or 8,5) or as it is
    /// </summary>
    private static bool Contains(HashSet<decimal> available, decimal number)
    {
        if (available.Contains(Math.Round(number, 6))) return true;
        return number < 1m && available.Contains(Math.Round(number * 100m, 6));
    }

    /// <summary>
    /// Verify one entry against the document of its single source
    /// </summary>
    public static SourceResult Verify(Entry entry, SourceDocument document) =>
        Verify(entry, [(entry.Urls.FirstOrDefault(), document)]);

    /// <summary>
    /// Verify one entry against the documents of its sources: a number is found when any readable
    /// document holds it
    /// </summary>
    /// <param name="entry">Lex entry</param>
    /// <param name="sources">Source URL and document, in the order of the entry source URLs</param>
    public static SourceResult Verify(Entry entry, IReadOnlyList<(string Url, SourceDocument Document)> sources)
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
        var numbers = ContentNumbers(entry.Content);
        if (numbers.Count == 0)
        {
            return new SourceResult { Key = entry.Key, Urls = urls, Match = SourceMatch.NoNumbers };
        }

        var perSection = readable
            .SelectMany(s => s.Document.Sections.Select(x => (s.Url, x.Heading, Numbers: SourceDocument.Numbers(x.Text))))
            .ToList();
        var all = new HashSet<decimal>(perSection.SelectMany(x => x.Numbers));
        var found = numbers.Where(n => Contains(all, n)).ToList();
        var missing = numbers.Except(found).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList();
        var match = found.Count == numbers.Count ? SourceMatch.Found
            : found.Count == 0 ? SourceMatch.NotFound
            : SourceMatch.Partial;

        string location = null;
        if (match == SourceMatch.Found)
        {
            // greedy cover: the sections that hold most of the numbers
            var remaining = new HashSet<decimal>(found);
            var cover = new List<(string Url, string Heading)>();
            while (remaining.Count > 0 && cover.Count <= MaxSections)
            {
                var best = perSection
                    .Select(s => (s.Url, s.Heading, Hits: remaining.Where(n => Contains(s.Numbers, n)).ToList()))
                    .OrderByDescending(x => x.Hits.Count).First();
                if (best.Hits.Count == 0) break;
                cover.Add((best.Url, best.Heading));
                remaining.ExceptWith(best.Hits);
            }
            if (remaining.Count == 0 && cover.Count <= MaxSections)
            {
                location = sources.Count == 1
                    ? Sections(cover.Select(x => x.Heading).ToList())
                    : string.Join("; ", cover.GroupBy(x => x.Url)
                        .Select(g => $"{Label(g.Key)}: {Sections(g.Select(x => x.Heading).ToList())}"));
            }
        }
        return new SourceResult
        {
            Key = entry.Key, Urls = urls, Match = match, NumbersTotal = numbers.Count,
            NumbersFound = found.Count, Missing = missing, Location = location, Reason = reason
        };
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
}
