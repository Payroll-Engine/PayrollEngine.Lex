using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PayrollEngine.Lex;

/// <summary>
/// Tolerant HTML tokenizer: extracts the text of a page as sections (headings h1-h4).
/// Script, style and comment blocks are dropped, block elements become line breaks and
/// table cells are separated by tabs, so that the numbers of one table row stay on one line.
/// </summary>
internal static class HtmlReader
{
    private const int MaxHeadingLength = 200;

    /// <summary>Heading of the text before the first heading</summary>
    internal const string StartHeading = "(start of page)";

    private static readonly HashSet<string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template"
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "br", "button", "caption", "dd", "details", "div", "dl", "dt",
        "fieldset", "figcaption", "figure", "footer", "form", "h5", "h6", "header", "hr", "label", "legend", "li",
        "main", "nav", "ol", "option", "p", "pre", "section", "summary", "table", "tbody", "tfoot", "thead", "tr", "ul"
    };

    private static readonly HashSet<string> CellElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "td", "th"
    };

    private static readonly Regex Spaces = new(@"[ \f\v\u00a0\u200b]+", RegexOptions.Compiled);

    /// <summary>
    /// Read the sections of an HTML page
    /// </summary>
    internal static List<(string Heading, string Text)> Read(string html)
    {
        var sections = new List<(string Heading, string Text)>();
        var heading = StartHeading;
        var text = new StringBuilder();
        StringBuilder headingText = null;
        var position = 0;

        while (position < html.Length)
        {
            var open = html.IndexOf('<', position);
            if (open < 0)
            {
                AppendText(headingText ?? text, html[position..]);
                break;
            }
            AppendText(headingText ?? text, html[position..open]);

            // comment
            if (string.CompareOrdinal(html, open, "<!--", 0, 4) == 0)
            {
                var end = html.IndexOf("-->", open + 4, StringComparison.Ordinal);
                position = end < 0 ? html.Length : end + 3;
                continue;
            }

            var close = TagEnd(html, open);
            if (close < 0)
            {
                // unterminated tag: the rest is text
                AppendText(headingText ?? text, html[open..]);
                break;
            }
            var (name, closing) = TagName(html, open + 1, close);
            position = close + 1;
            if (name.Length == 0)
            {
                continue;
            }

            if (!closing && SkippedElements.Contains(name))
            {
                var end = html.IndexOf("</" + name, position, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    break;
                }
                var endClose = html.IndexOf('>', end);
                position = endClose < 0 ? html.Length : endClose + 1;
                continue;
            }

            if (IsSectionHeading(name))
            {
                if (!closing)
                {
                    if (headingText != null)
                    {
                        // unclosed heading: the next heading ends it
                        heading = HeadingOf(headingText, heading);
                    }
                    AddSection(sections, heading, text);
                    headingText = new StringBuilder();
                }
                else if (headingText != null)
                {
                    heading = HeadingOf(headingText, heading);
                    headingText = null;
                }
                continue;
            }

            var target = headingText ?? text;
            if (CellElements.Contains(name))
            {
                if (!closing) target.Append('\t');
            }
            else if (BlockElements.Contains(name))
            {
                target.Append('\n');
            }
            else
            {
                // inline element: keep words apart
                target.Append(' ');
            }
        }

        if (headingText != null)
        {
            heading = HeadingOf(headingText, heading);
        }
        AddSection(sections, heading, text);
        return sections;
    }

    /// <summary>
    /// Normalize extracted text: decoded entities, one space between words, tabs between cells, one line per block
    /// </summary>
    internal static string CleanLines(string text)
    {
        var lines = new List<string>();
        foreach (var rawLine in text.Replace("\r", string.Empty).Split('\n'))
        {
            var cells = new List<string>();
            foreach (var cell in rawLine.Split('\t'))
            {
                var value = Spaces.Replace(cell, " ").Trim();
                if (value.Length > 0)
                {
                    cells.Add(value);
                }
            }
            if (cells.Count > 0)
            {
                lines.Add(string.Join('\t', cells));
            }
        }
        return string.Join('\n', lines);
    }

    private static bool IsSectionHeading(string name) =>
        name.Length == 2 && (name[0] == 'h' || name[0] == 'H') && name[1] is >= '1' and <= '4';

    private static string HeadingOf(StringBuilder headingText, string previous)
    {
        var value = CleanLines(headingText.ToString()).Replace('\n', ' ').Replace('\t', ' ');
        if (value.Length == 0)
        {
            return previous;
        }
        return value.Length > MaxHeadingLength ? value[..MaxHeadingLength] : value;
    }

    private static void AddSection(List<(string Heading, string Text)> sections, string heading, StringBuilder text)
    {
        var value = CleanLines(text.ToString());
        text.Clear();
        if (value.Length > 0)
        {
            sections.Add((heading, value));
        }
    }

    private static void AppendText(StringBuilder target, string raw)
    {
        if (raw.Length == 0)
        {
            return;
        }
        // line breaks in the source are layout, not structure
        target.Append(WebUtility.HtmlDecode(raw.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ')));
    }

    // ReSharper disable once GrammarMistakeInComment
    /// <summary>
    /// Position of the closing '&gt;' of a tag, quoted attribute values may contain '&gt;'
    /// </summary>
    private static int TagEnd(string html, int open)
    {
        char quote = '\0';
        for (var i = open + 1; i < html.Length; i++)
        {
            var c = html[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'')
            {
                // quotes only delimit attribute values after '='
                var previous = i - 1;
                while (previous > open && char.IsWhiteSpace(html[previous])) previous--;
                if (html[previous] == '=') quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }
        return -1;
    }

    private static (string Name, bool Closing) TagName(string html, int start, int end)
    {
        var i = start;
        var closing = i < end && html[i] == '/';
        if (closing) i++;
        var nameStart = i;
        while (i < end && (char.IsLetterOrDigit(html[i]) || html[i] is '-' or ':')) i++;
        return (html[nameStart..i], closing);
    }
}
