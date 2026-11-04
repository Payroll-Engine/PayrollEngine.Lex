using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Readability of a source document
/// </summary>
internal enum LexSourceState
{
    /// <summary>Text could be extracted</summary>
    Readable,

    /// <summary>The page could not be requested or holds no text (blocked host, script-only page)</summary>
    Unreadable,

    /// <summary>The format cannot be read (e.g. PDF)</summary>
    Unsupported
}

/// <summary>
/// Text of a source document, split into sections (HTML headings, spreadsheet sheets)
/// </summary>
public sealed class LexSourceDocument
{
    private const int MinimumTextLength = 400;

    internal LexSourceState State { get; init; }
    internal string Reason { get; init; }
    internal List<(string Heading, string Text)> Sections { get; init; } = [];

    /// <summary>
    /// Numbers of one section as normalized decimal values (every separator interpretation)
    /// </summary>
    internal static HashSet<decimal> Numbers(string text)
    {
        var values = new HashSet<decimal>();
        foreach (Match match in NumberToken.Matches(text))
        {
            foreach (var value in Interpretations(match.Value))
            {
                values.Add(Math.Round(value, 6));
            }
        }
        return values;
    }

    private static readonly Regex NumberToken = new(
        @"\d{1,3}(?:[ \u00a0\u202f.,'’]\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?", RegexOptions.Compiled);

    private static IEnumerable<decimal> Interpretations(string token)
    {
        // integer reading: every separator is a group separator
        var digits = new string(token.Where(char.IsDigit).ToArray());
        if (digits.Length is > 0 and <= 15 &&
            decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
        {
            yield return integer;
        }
        // decimal reading: the last '.' or ',' is the decimal separator when 1-4 digits follow
        var last = token.LastIndexOfAny(['.', ',']);
        if (last > 0 && token.Length - last - 1 is >= 1 and <= 4)
        {
            var whole = new string(token[..last].Where(char.IsDigit).ToArray());
            var fraction = token[(last + 1)..];
            if (decimal.TryParse($"{whole}.{fraction}", NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                yield return number;
            }
        }
    }

    /// <summary>
    /// Build a document from HTML
    /// </summary>
    internal static LexSourceDocument FromHtml(string html)
    {
        html = Regex.Replace(html, @"<(script|style|noscript)\b.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var sections = new List<(string, string)>();
        var heading = "(start of page)";
        var position = 0;
        foreach (Match match in Regex.Matches(html, @"<h([1-4])\b[^>]*>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            AddSection(sections, heading, html[position..match.Index]);
            heading = CleanText(match.Groups[2].Value);
            position = match.Index + match.Length;
        }
        AddSection(sections, heading, html[position..]);
        return Create(sections);
    }

    /// <summary>
    /// Build a document from plain text
    /// </summary>
    internal static LexSourceDocument FromText(string text) =>
        Create([("(text)", CleanText(text))]);

    /// <summary>
    /// Load the source: a local evidence file when present, otherwise the URL
    /// </summary>
    /// <param name="url">Source URL</param>
    /// <param name="localPath">Local evidence file (optional)</param>
    /// <param name="client">HTTP client for the source URL</param>
    /// <param name="readers">Readers for formats without a built-in reader, by lower-case file extension
    /// (e.g. <c>.xlsx</c>). A reader returns the text sections of the document.</param>
    public static async Task<LexSourceDocument> Load(string url, string localPath, HttpClient client,
        IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers = null)
    {
        try
        {
            var name = localPath ?? url;
            var extension = Path.GetExtension(name.Split('?')[0]).ToLowerInvariant();
            if (extension == ".pdf")
            {
                return new LexSourceDocument { State = LexSourceState.Unsupported, Reason = "PDF" };
            }
            byte[] bytes;
            if (localPath != null && File.Exists(localPath))
            {
                bytes = await File.ReadAllBytesAsync(localPath);
            }
            else
            {
                using var response = await client.GetAsync(url);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return new LexSourceDocument { State = LexSourceState.Unreadable, Reason = $"HTTP {(int)response.StatusCode}" };
                }
                bytes = await response.Content.ReadAsByteArrayAsync();
                var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (type.Contains("pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return new LexSourceDocument { State = LexSourceState.Unsupported, Reason = "PDF" };
                }
                // ReSharper disable once StringLiteralTypo
                if (type.Contains("spreadsheetml", StringComparison.OrdinalIgnoreCase)) extension = ".xlsx";
            }
            if (readers != null && readers.TryGetValue(extension, out var reader))
            {
                return new LexSourceDocument { State = LexSourceState.Readable, Sections = reader(new MemoryStream(bytes)) };
            }
            var text = Encoding.UTF8.GetString(bytes);
            return extension is ".txt" or ".csv" ? FromText(text) : FromHtml(text);
        }
        catch (Exception exception)
        {
            return new LexSourceDocument { State = LexSourceState.Unreadable, Reason = exception.Message };
        }
    }

    private static LexSourceDocument Create(List<(string Heading, string Text)> sections)
    {
        var length = sections.Sum(x => x.Text.Length);
        return length < MinimumTextLength
            ? new LexSourceDocument { State = LexSourceState.Unreadable, Reason = "no readable text (script-only page?)" }
            : new LexSourceDocument { State = LexSourceState.Readable, Sections = sections };
    }

    private static void AddSection(List<(string, string)> sections, string heading, string html)
    {
        var text = CleanText(html);
        if (text.Length > 0)
        {
            sections.Add((heading, text));
        }
    }

    private static string CleanText(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();
}
