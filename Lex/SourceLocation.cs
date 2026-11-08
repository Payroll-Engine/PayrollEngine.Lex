using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Source location of a lex entry: covering sections, labels and canonical text
/// </summary>
public static class SourceLocation
{
    private const int MaxSectionsPerSource = 3;

    /// <summary>Note of a found entry whose location exceeds the maximum location length</summary>
    internal const string LocationTooLongNote = "location too long: set sourceLocation manually";

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
    /// Source location of the sections that hold the numbers (greedy cover: the sections with most of the numbers,
    /// at most <see cref="MaxSectionsPerSource"/> per source), null when the numbers are not covered
    /// </summary>
    /// <param name="sources">Sources of the entry</param>
    /// <param name="sections">Sections to search</param>
    /// <param name="numbers">Numbers to cover</param>
    /// <param name="percent">Lookup of a value below 1 as percentage</param>
    /// <param name="first">Section that starts the cover (section of the source quote), null for none</param>
    internal static string CoverLocation(IReadOnlyList<(string Url, SourceDocument Document)> sources,
        List<SourceSection> sections, IEnumerable<decimal> numbers, PercentLookup percent, SourceSection first = null)
    {
        var remaining = new HashSet<decimal>(numbers);
        var cover = new List<SourceSection>();
        var perSource = new Dictionary<string, int>(StringComparer.Ordinal);
        void AddToCover(SourceSection section)
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
            if (best.Section == null || best.Hits == 0)
            {
                break;
            }
            AddToCover(best.Section);
        }
        return remaining.Count == 0 ? Location(sources, cover) : null;
    }

    /// <summary>
    /// Source location of the covering sections; with several sources one part per source, in the order of the
    /// sources, named by the evidence file or, without file name, by the host
    /// </summary>
    internal static string Location(IReadOnlyList<(string Url, SourceDocument Document)> sources, List<SourceSection> cover)
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
    internal static string Label(string url, SourceDocument document) =>
        string.IsNullOrWhiteSpace(document?.Name) ? Label(url) : document.Name;

    private static string Sections(List<string> headings) =>
        headings.Count == 1
            ? $"Section \"{headings[0]}\""
            : "Sections " + string.Join(", ", headings.Select(h => $"\"{h}\""));

    /// <summary>
    /// Short source name: the host of a URL without www, otherwise the text
    /// </summary>
    internal static string Label(string url) =>
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
}
