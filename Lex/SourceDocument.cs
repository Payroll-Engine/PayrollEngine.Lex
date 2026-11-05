using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Readability of a source document
/// </summary>
internal enum SourceState
{
    /// <summary>Text could be extracted</summary>
    Readable,

    /// <summary>The source could not be requested or holds no text (blocked host, script-only page,
    /// PDF without text layer, encrypted PDF)</summary>
    Unreadable,

    /// <summary>The format has no reader (e.g. a ZIP archive or an unknown binary format)</summary>
    Unsupported,

    /// <summary>No local evidence file exists</summary>
    Missing
}

/// <summary>
/// Text of a source document, split into sections (document headings, spreadsheet sheets, PDF pages)
/// </summary>
public sealed class SourceDocument
{
    private const int MinimumTextLength = 400;

    internal SourceState State { get; init; }
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
            if (match.Groups["lakh"].Success)
            {
                // the grouping is unambiguous: one reading only
                if (decimal.TryParse(match.Value.Replace(",", string.Empty), NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var lakh))
                {
                    values.Add(Math.Round(lakh, 6));
                }
                continue;
            }
            foreach (var value in Interpretations(match.Value))
            {
                values.Add(Math.Round(value, 6));
            }
        }
        return values;
    }

    // lakh grouping (1,50,000 and 20,00,000.50): groups of two digits before the last group of three
    private static readonly Regex NumberToken = new(
        @"(?<lakh>\d{1,2}(?:,\d{2})+,\d{3}(?:\.\d+)?)(?!\d|,\d)|\d{1,3}(?:[ \u00a0\u202f.,'’]\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?",
        RegexOptions.Compiled);

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
    internal static SourceDocument FromHtml(string html) =>
        Create(HtmlReader.Read(html));

    /// <summary>
    /// Build a document from plain text
    /// </summary>
    internal static SourceDocument FromText(string text) =>
        Create([("(text)", HtmlReader.CleanLines(text))]);

    /// <summary>
    /// User agent of all lex source requests: snapshot and check must request a source the same way
    /// </summary>
    internal const string UserAgent = "Mozilla/5.0 (compatible; PayrollEngine-Lex)";

    /// <summary>
    /// HTTP client for source requests
    /// </summary>
    /// <param name="timeout">Request timeout (default: 60 seconds)</param>
    public static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>
    /// True if text could be extracted
    /// </summary>
    public bool IsReadable => State == SourceState.Readable;

    /// <summary>
    /// Load the local evidence file of an entry; a missing file gives a document without evidence
    /// </summary>
    /// <param name="localPath">Local evidence file, null when the entry has none</param>
    /// <param name="readers">Additional readers by lower-case file extension (see <see cref="Load"/>)</param>
    public static Task<SourceDocument> LoadLocal(string localPath,
        IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers = null)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            return Task.FromResult(new SourceDocument { State = SourceState.Missing, Reason = "no local evidence" });
        }
        return Load(null, localPath, null, readers);
    }

    /// <summary>
    /// Load the live source from its URL
    /// </summary>
    /// <param name="url">Source URL</param>
    /// <param name="client">HTTP client</param>
    /// <param name="readers">Additional readers by lower-case file extension (see <see cref="Load"/>)</param>
    public static Task<SourceDocument> LoadLive(string url, HttpClient client,
        IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers = null) =>
        Load(url, null, client, readers);

    /// <summary>
    /// Text used for change detection: the lines of all sections that hold a number (values), without date and
    /// time stamps and without whitespace (markup changes move spaces around inline elements), sorted, because
    /// pages may serve their blocks in a varying order. Lines without a number (menus, navigation) are ignored.
    /// A table row is one line, so a changed value still changes the text.
    /// </summary>
    internal string NormalizedText()
    {
        var lines = new List<string>();
        foreach (var (heading, body) in Sections)
        {
            AddNormalizedLine(lines, heading);
            foreach (var line in body.Split('\n'))
            {
                AddNormalizedLine(lines, line);
            }
        }
        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines);
    }

    private static void AddNormalizedLine(List<string> lines, string line)
    {
        foreach (var pattern in DatePatterns)
        {
            line = pattern.Replace(line, " ");
        }
        line = Whitespace.Replace(line, string.Empty);
        if (line.Any(char.IsAsciiDigit))
        {
            lines.Add(line);
        }
    }

    /// <summary>
    /// Hash of the normalized text (sha256:...), null when the document is not readable
    /// </summary>
    internal string TextHash() =>
        IsReadable ? "sha256:" + ContentHash.StringHash(NormalizedText()) : null;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private const string MonthNames =
        "Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?";

    private static readonly Regex[] DatePatterns =
    [
        // ISO date with optional time
        new(@"\b\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?\b", RegexOptions.Compiled),
        // numeric dates: 03.11.2026, 3/11/2026, 03-11-26
        new(@"\b\d{1,2}[./-]\d{1,2}[./-](?:\d{4}|\d{2})\b", RegexOptions.Compiled),
        // 3 November 2026, 3rd Nov. 2026
        new($@"\b\d{{1,2}}(?:st|nd|rd|th)?\.?\s+(?:{MonthNames})\.?,?\s+\d{{4}}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // November 3, 2026
        new($@"\b(?:{MonthNames})\.?\s+\d{{1,2}}(?:st|nd|rd|th)?,?\s+\d{{4}}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // times: 14:05, 2:05:10 pm
        new(@"\b\d{1,2}:\d{2}(?::\d{2})?(?:\s*[ap]\.?m\.?)?(?![\w:])", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    ];

    /// <summary>
    /// Built-in readers by detected format (see <see cref="FormatDetector.Detect"/>)
    /// </summary>
    private static readonly Dictionary<string, Func<Stream, List<(string Heading, string Text)>>> BuiltInReaders = new()
    {
        [".xls"] = SpreadsheetReader.Read,
        [".xlsx"] = SpreadsheetReader.Read,
        [".xlsb"] = SpreadsheetReader.Read,
        [".pdf"] = PdfReader.Read,
        [".ods"] = OfficeXmlReader.ReadOds,
        [".odt"] = OfficeXmlReader.ReadOdt,
        [".docx"] = OfficeXmlReader.ReadDocx
    };

    private static Func<Stream, List<(string Heading, string Text)>> PassedReader(string extension,
        IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers) =>
        readers != null && !string.IsNullOrEmpty(extension) && readers.TryGetValue(extension, out var reader) ? reader : null;

    /// <summary>
    /// Load the source: a local evidence file when present, otherwise the URL
    /// </summary>
    /// <remarks>The format is detected from the content, not from the file extension or media type.
    /// Built-in formats: HTML, text (<c>.txt</c>, <c>.csv</c>), spreadsheets (<c>.xls</c>, <c>.xlsx</c>,
    /// <c>.xlsb</c>, <c>.ods</c>, one section per sheet), PDF (text layer, one section per page) and
    /// text documents (<c>.docx</c>, <c>.odt</c>, one section per heading). Other binary content is unsupported.</remarks>
    /// <param name="url">Source URL</param>
    /// <param name="localPath">Local evidence file (optional)</param>
    /// <param name="client">HTTP client for the source URL</param>
    /// <param name="readers">Additional readers by lower-case file extension (e.g. <c>.ods</c>). A passed reader
    /// for the detected format replaces the built-in reader, a passed reader for the file extension reads content
    /// of a format without built-in reader. A reader returns the text sections of the document.</param>
    public static async Task<SourceDocument> Load(string url, string localPath, HttpClient client,
        IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers = null)
    {
        try
        {
            var name = localPath ?? url;
            var extension = Path.GetExtension(name.Split('?')[0]).ToLowerInvariant();
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
                    return new SourceDocument { State = SourceState.Unreadable, Reason = $"HTTP {(int)response.StatusCode}" };
                }
                bytes = await response.Content.ReadAsByteArrayAsync();
            }
            return Read(bytes, extension, readers);
        }
        catch (Exception exception)
        {
            return new SourceDocument { State = SourceState.Unreadable, Reason = exception.Message };
        }
    }

    /// <summary>
    /// Read the content: a passed reader for the detected format or the file extension, otherwise the built-in
    /// reader of the detected format; content without known signature is read as text or HTML unless it is binary
    /// </summary>
    private static SourceDocument Read(byte[] bytes, string extension,
        IReadOnlyDictionary<string, Func<Stream, List<(string Heading, string Text)>>> readers)
    {
        var format = FormatDetector.Detect(bytes);
        var reader = (format != null ? PassedReader(format, readers) : null) ??
                     PassedReader(extension, readers) ??
                     (format != null ? BuiltInReaders.GetValueOrDefault(format) : null);
        if (reader != null)
        {
            return new SourceDocument { State = SourceState.Readable, Sections = reader(new MemoryStream(bytes)) };
        }
        if (format != null)
        {
            return new SourceDocument { State = SourceState.Unsupported, Reason = FormatDetector.Name(format) };
        }
        if (FormatDetector.IsBinary(bytes))
        {
            return new SourceDocument { State = SourceState.Unsupported, Reason = "unknown binary format" };
        }
        var text = FormatDetector.DecodeText(bytes);
        return extension is ".txt" or ".csv" ? FromText(text) : FromHtml(text);
    }

    private static SourceDocument Create(List<(string Heading, string Text)> sections)
    {
        var length = sections.Sum(x => x.Text.Length);
        return length < MinimumTextLength
            ? new SourceDocument { State = SourceState.Unreadable, Reason = "no readable text (script-only page?)" }
            : new SourceDocument { State = SourceState.Readable, Sections = sections };
    }
}
