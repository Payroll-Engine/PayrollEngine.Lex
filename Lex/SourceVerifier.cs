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

    /// <summary>The source cannot be read (blocked, script-only page, PDF)</summary>
    Unreadable,

    /// <summary>The entry holds no numbers to look for</summary>
    NoNumbers,

    /// <summary>The entry has no local evidence file (local verification)</summary>
    NoEvidence
}

/// <summary>
/// Result for one entry
/// </summary>
public sealed class SourceResult
{
    /// <summary>Key of the lex entry</summary>
    public string Key { get; internal init; }

    /// <summary>Source URL of the lex entry</summary>
    public string Url { get; internal init; }

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

    /// <summary>Reason of an unreadable source</summary>
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
    /// Numbers of an entry content (scalars, object properties, array items); 0 and 1 carry no information
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
        if (value > 1m || (value > 0m && value < 1m))
        {
            numbers.Add(value);
        }
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
    /// Verify one entry against a source document
    /// </summary>
    public static SourceResult Verify(Entry entry, SourceDocument document)
    {
        if (document.State != SourceState.Readable)
        {
            return new SourceResult
            {
                Key = entry.Key, Url = entry.SourceUrl, Reason = document.Reason,
                Match = document.State == SourceState.Missing ? SourceMatch.NoEvidence : SourceMatch.Unreadable
            };
        }
        var numbers = ContentNumbers(entry.Content);
        if (numbers.Count == 0)
        {
            return new SourceResult { Key = entry.Key, Url = entry.SourceUrl, Match = SourceMatch.NoNumbers };
        }

        var perSection = document.Sections.Select(s => (s.Heading, Numbers: SourceDocument.Numbers(s.Text))).ToList();
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
            var headings = new List<string>();
            while (remaining.Count > 0 && headings.Count <= MaxSections)
            {
                var best = perSection
                    .Select(s => (s.Heading, Hits: remaining.Where(n => Contains(s.Numbers, n)).ToList()))
                    .OrderByDescending(x => x.Hits.Count).First();
                if (best.Hits.Count == 0) break;
                headings.Add(best.Heading);
                remaining.ExceptWith(best.Hits);
            }
            if (remaining.Count == 0 && headings.Count <= MaxSections)
            {
                location = headings.Count == 1
                    ? $"Section \"{headings[0]}\""
                    : "Sections " + string.Join(", ", headings.Select(h => $"\"{h}\""));
            }
        }
        return new SourceResult
        {
            Key = entry.Key, Url = entry.SourceUrl, Match = match, NumbersTotal = numbers.Count,
            NumbersFound = found.Count, Missing = missing, Location = location
        };
    }
}
