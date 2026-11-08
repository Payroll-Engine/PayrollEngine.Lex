using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace PayrollEngine.Lex;

/// <summary>
/// Reads the text of office XML documents without office library: OpenDocument spreadsheet (ods, one section
/// per sheet), OpenDocument text (odt) and Word document (docx), one section per heading (outline level 1-4).
/// Table rows are one line, table cells are separated by tabs.
/// </summary>
internal static class OfficeXmlReader
{
    /// <summary>Maximum uncompressed size of a document part in megabytes</summary>
    internal const int MaxPartMegabytes = 100;

    /// <summary>Maximum number of rows read per sheet</summary>
    private const int MaxRows = 5000;

    /// <summary>Maximum repetitions of a repeated row or cell with a value</summary>
    private const int MaxRepeat = 100;

    /// <summary>Maximum length of a section heading; longer heading paragraphs are cut</summary>
    private const int MaxHeadingLength = 200;
    /// <summary>Maximum depth of the basedOn chain of a Word style (protects against cycles)</summary>
    private const int MaxStyleDepth = 10;

    /// <summary>Heading of the text before the first heading</summary>
    internal const string StartHeading = "(start of document)";

    // XML namespaces of OpenDocument (office, table, text) and Office Open XML (Word, markup compatibility)
    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string CompatibilityNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>Safe XML settings: no DTD and no external resolver (XXE protection)</summary>
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = true
    };

    #region OpenDocument spreadsheet

    /// <summary>
    /// Text of an OpenDocument spreadsheet: section heading is the sheet name, one line per row,
    /// numbers in invariant format, dates as serial number (same as the spreadsheet reader)
    /// </summary>
    internal static List<(string Heading, string Text)> ReadOds(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var xml = OpenPart(archive, "content.xml");
        var sections = new List<(string Heading, string Text)>();
        string sheetName = null;
        StringBuilder sheet = null;
        StringBuilder row = null;
        var rowRepeat = 1;
        var rows = 0;

        // a repeated row (number-rows-repeated) is written once per repetition, limited by MaxRepeat and MaxRows
        void EndRow()
        {
            if (row is { Length: > 0 } && sheet != null)
            {
                for (var i = 0; i < Math.Min(rowRepeat, MaxRepeat) && rows < MaxRows; i++, rows++)
                {
                    sheet.Append(row).Append('\n');
                }
            }
            row = null;
        }

        xml.Read();
        while (!xml.EOF)
        {
            if (xml.NamespaceURI == TableNs && xml.NodeType == XmlNodeType.Element)
            {
                switch (xml.LocalName)
                {
                    // a new sheet; sheets without name are numbered
                    case "table":
                        sheetName = xml.GetAttribute("name", TableNs) ?? $"Sheet {sections.Count + 1}";
                        sheet = new StringBuilder();
                        rows = 0;
                        if (xml.IsEmptyElement)
                        {
                            sections.Add((sheetName, string.Empty));
                            sheet = null;
                        }
                        break;
                    // an empty row element ends the row at once
                    case "table-row":
                        row = new StringBuilder();
                        rowRepeat = Repeat(xml, "number-rows-repeated");
                        if (xml.IsEmptyElement)
                        {
                            EndRow();
                        }
                        break;
                    case "table-cell":
                    // covered cells (part of a merged range) are read like normal cells
                    case "covered-table-cell":
                        var repeat = Repeat(xml, "number-columns-repeated");
                        // reads the cell content
                        var value = CellValue(xml);
                        if (value != null && row != null)
                        {
                            for (var i = 0; i < Math.Min(repeat, MaxRepeat); i++)
                            {
                                row.Append(value).Append(' ');
                            }
                        }
                        // CellValue positioned the reader after the cell: do not read again
                        continue;
                }
            }
            // end of a row or sheet: flush the collected text
            else if (xml.NamespaceURI == TableNs && xml.NodeType == XmlNodeType.EndElement)
            {
                if (xml.LocalName == "table-row")
                {
                    EndRow();
                }
                else if (xml.LocalName == "table" && sheet != null)
                {
                    sections.Add((sheetName, sheet.ToString()));
                    sheet = null;
                }
            }
            xml.Read();
        }
        return sections;
    }

    /// <summary>
    /// Repeat count of a row or cell attribute, 1 if missing or invalid
    /// </summary>
    private static int Repeat(XmlReader xml, string name) =>
        int.TryParse(xml.GetAttribute(name, TableNs), NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 1
            ? count
            : 1;

    /// <summary>
    /// Value of a cell: numbers, dates and times from the typed value, other cells by their text.
    /// Positions the reader after the cell.
    /// </summary>
    private static string CellValue(XmlReader xml)
    {
        // typed values are written in invariant format so that "1.234,50" in a localized sheet matches 1234.5
        var type = xml.GetAttribute("value-type", OfficeNs);
        var value = type switch
        {
            "float" or "percentage" or "currency" => Number(xml.GetAttribute("value", OfficeNs)),
            "date" => Date(xml.GetAttribute("date-value", OfficeNs)),
            "time" => Time(xml.GetAttribute("time-value", OfficeNs)),
            _ => null
        };
        // the text is read in any case: it positions the reader after the cell
        var text = CellText(xml);
        // boolean values hold no text
        if (type == "boolean")
        {
            return null;
        }
        return value ?? (text.Length > 0 ? text : null);
    }

    /// <summary>
    /// Display text of a cell without annotations; paragraphs and spacing elements become spaces.
    /// Positions the reader after the cell.
    /// </summary>
    private static string CellText(XmlReader xml)
    {
        if (xml.IsEmptyElement)
        {
            xml.Read();
            return string.Empty;
        }
        var text = new StringBuilder();
        using (var cell = xml.ReadSubtree())
        {
            // first Read: the cell element itself, second Read: its first child
            cell.Read();
            cell.Read();
            while (!cell.EOF)
            {
                if (cell.NodeType == XmlNodeType.Element && cell.NamespaceURI == OfficeNs && cell.LocalName == "annotation")
                {
                    cell.Skip();
                    continue;
                }
                if (cell.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
                {
                    text.Append(cell.Value);
                }
                else if (cell.NamespaceURI == TextNs &&
                         (cell.NodeType == XmlNodeType.EndElement && cell.LocalName is "p" or "h" ||
                          cell.NodeType == XmlNodeType.Element && cell.LocalName is "s" or "tab" or "line-break"))
                {
                    text.Append(' ');
                }
                cell.Read();
            }
        }
        // the subtree reader leaves the reader on the cell end element
        xml.Read();
        return HtmlReader.CleanLines(text.ToString().Replace('\t', ' ')).Replace('\n', ' ');
    }

    /// <summary>
    /// Invariant number text of an attribute value, null if no number
    /// </summary>
    private static string Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Number(number) : null;

    /// <summary>
    /// Number with at most six decimals, without exponent
    /// </summary>
    private static string Number(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Date as OLE automation serial number, like a spreadsheet stores it
    /// </summary>
    private static string Date(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? Number(date.ToOADate()) : null;

    /// <summary>
    /// Time (ISO 8601 duration) as fraction of a day, null if invalid
    /// </summary>
    private static string Time(string value)
    {
        try
        {
            return value == null ? null : Number(XmlConvert.ToTimeSpan(value).TotalDays);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    #endregion

    #region OpenDocument text

    /// <summary>
    /// Text of an OpenDocument text: sections by heading, one line per paragraph and table row.
    /// Annotations and tracked changes are skipped.
    /// </summary>
    internal static List<(string Heading, string Text)> ReadOdt(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var xml = OpenPart(archive, "content.xml");
        // the flow builder turns paragraphs, headings and table rows into sections
        var flow = new FlowBuilder();
        xml.Read();
        while (!xml.EOF)
        {
            if (xml.NodeType == XmlNodeType.Element)
            {
                if (xml.NamespaceURI == OfficeNs && xml.LocalName == "annotation" ||
                    xml.NamespaceURI == TextNs && xml.LocalName == "tracked-changes")
                {
                    xml.Skip();
                    continue;
                }
                if (xml.NamespaceURI == TextNs)
                {
                    switch (xml.LocalName)
                    {
                        // outline level 1 is the top heading: the flow builder counts levels from 0
                        case "h":
                            var level = int.TryParse(xml.GetAttribute("outline-level", TextNs), NumberStyles.None,
                                CultureInfo.InvariantCulture, out var outline) ? outline : 1;
                            flow.StartParagraph(level - 1);
                            if (xml.IsEmptyElement)
                            {
                                flow.EndParagraph();
                            }
                            break;
                        case "p":
                            flow.StartParagraph();
                            if (xml.IsEmptyElement)
                            {
                                flow.EndParagraph();
                            }
                            break;
                        case "s":
                        case "tab":
                            flow.Append(" ");
                            break;
                        // a manual line break keeps the line structure outside tables
                        case "line-break":
                            flow.LineBreak();
                            break;
                    }
                }
                else if (xml.NamespaceURI == TableNs)
                {
                    switch (xml.LocalName)
                    {
                        case "table":
                            flow.StartTable();
                            if (xml.IsEmptyElement)
                            {
                                flow.EndTable();
                            }
                            break;
                        case "table-cell":
                        case "covered-table-cell":
                            flow.StartCell();
                            break;
                    }
                }
            }
            else if (xml.NodeType == XmlNodeType.EndElement)
            {
                if (xml.NamespaceURI == TextNs && xml.LocalName is "p" or "h")
                {
                    flow.EndParagraph();
                }
                else if (xml.NamespaceURI == TableNs && xml.LocalName == "table-row")
                {
                    flow.EndRow();
                }
                else if (xml.NamespaceURI == TableNs && xml.LocalName == "table")
                {
                    flow.EndTable();
                }
            }
            // text outside paragraphs (e.g. in frames without paragraph) is ignored
            else if (flow.InParagraph && xml.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
            {
                flow.AppendText(xml.Value);
            }
            xml.Read();
        }
        return flow.Finish();
    }

    #endregion

    #region Word document

    /// <summary>
    /// Text of a Word document body: sections by heading (outline level of the paragraph or its style),
    /// one line per paragraph and table row. Deleted text, field codes and fallback content are skipped;
    /// headers, footers and notes are not read.
    /// </summary>
    internal static List<(string Heading, string Text)> ReadDocx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        // headings in Word are usually styles: their outline levels are resolved before reading the body
        var styles = StyleOutlineLevels(archive);
        using var xml = OpenPart(archive, "word/document.xml");
        var flow = new FlowBuilder();
        // text nodes count only inside w:t elements (field codes are in w:instrText)
        var inText = false;
        xml.Read();
        while (!xml.EOF)
        {
            if (xml.NodeType == XmlNodeType.Element)
            {
                // fallback content repeats the main content for old readers; change records hold the previous formatting
                if (xml.NamespaceURI == CompatibilityNs && xml.LocalName == "Fallback" ||
                    xml.NamespaceURI == WordNs && xml.LocalName is "pPrChange" or "rPrChange")
                {
                    xml.Skip();
                    continue;
                }
                if (xml.NamespaceURI == WordNs)
                {
                    switch (xml.LocalName)
                    {
                        case "p":
                            flow.StartParagraph();
                            if (xml.IsEmptyElement)
                            {
                                flow.EndParagraph();
                            }
                            break;
                        // the style of the paragraph gives its outline level, a direct outlineLvl overrides it
                        case "pStyle":
                            if (styles.TryGetValue(xml.GetAttribute("val", WordNs) ?? string.Empty, out var styleLevel))
                            {
                                flow.SetOutlineLevel(styleLevel);
                            }
                            break;
                        case "outlineLvl":
                            if (int.TryParse(xml.GetAttribute("val", WordNs), NumberStyles.None, CultureInfo.InvariantCulture, out var level))
                            {
                                flow.SetOutlineLevel(level);
                            }
                            break;
                        case "t":
                            inText = !xml.IsEmptyElement;
                            break;
                        case "tab":
                            flow.Append(" ");
                            break;
                        case "br":
                        case "cr":
                            flow.LineBreak();
                            break;
                        // a non-breaking hyphen is a character element, not text
                        case "noBreakHyphen":
                            flow.Append("-");
                            break;
                        case "tbl":
                            flow.StartTable();
                            break;
                        case "tc":
                            flow.StartCell();
                            break;
                    }
                }
            }
            else if (xml.NodeType == XmlNodeType.EndElement && xml.NamespaceURI == WordNs)
            {
                switch (xml.LocalName)
                {
                    case "p":
                        flow.EndParagraph();
                        break;
                    case "t":
                        inText = false;
                        break;
                    case "tr":
                        flow.EndRow();
                        break;
                    case "tbl":
                        flow.EndTable();
                        break;
                }
            }
            else if (inText && xml.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
            {
                flow.AppendText(xml.Value);
            }
            xml.Read();
        }
        return flow.Finish();
    }

    /// <summary>
    /// Outline level of the paragraph styles by style id, including inherited levels (basedOn)
    /// and built-in heading names (heading 1-9)
    /// </summary>
    private static Dictionary<string, int> StyleOutlineLevels(ZipArchive archive)
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        if (archive.GetEntry("word/styles.xml") == null)
        {
            return levels;
        }
        // first pass: direct level and base style of each paragraph style
        var styles = new Dictionary<string, (string BasedOn, int? Level)>(StringComparer.Ordinal);
        using (var xml = OpenPart(archive, "word/styles.xml"))
        {
            string id = null;
            string basedOn = null;
            int? level = null;
            while (xml.Read())
            {
                if (xml.NamespaceURI != WordNs)
                {
                    continue;
                }
                if (xml.NodeType == XmlNodeType.Element)
                {
                    switch (xml.LocalName)
                    {
                        // only paragraph styles carry outline levels (character and table styles are ignored)
                        case "style":
                            id = xml.GetAttribute("type", WordNs) is null or "paragraph" ? xml.GetAttribute("styleId", WordNs) : null;
                            basedOn = null;
                            level = null;
                            break;
                        // built-in heading styles are recognized by their name, even without outlineLvl
                        case "name" when id != null && level == null:
                            var name = xml.GetAttribute("val", WordNs) ?? string.Empty;
                            if (name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) &&
                                int.TryParse(name[8..], NumberStyles.None, CultureInfo.InvariantCulture, out var heading))
                            {
                                level = heading - 1;
                            }
                            break;
                        case "basedOn" when id != null:
                            basedOn = xml.GetAttribute("val", WordNs);
                            break;
                        case "outlineLvl" when id != null:
                            if (int.TryParse(xml.GetAttribute("val", WordNs), NumberStyles.None, CultureInfo.InvariantCulture, out var outline))
                            {
                                level = outline;
                            }
                            break;
                    }
                }
                else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "style" && id != null)
                {
                    styles[id] = (basedOn, level);
                    id = null;
                }
            }
        }
        // second pass: inherit the level from the basedOn chain
        foreach (var styleId in styles.Keys)
        {
            var current = styleId;
            for (var depth = 0; current != null && depth < MaxStyleDepth; depth++)
            {
                if (!styles.TryGetValue(current, out var style))
                {
                    break;
                }
                if (style.Level.HasValue)
                {
                    levels[styleId] = style.Level.Value;
                    break;
                }
                current = style.BasedOn;
            }
        }
        return levels;
    }

    #endregion

    /// <summary>
    /// XML reader of a document part, limited in size
    /// </summary>
    private static XmlReader OpenPart(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"{name} not found");
        const long limit = MaxPartMegabytes * 1024L * 1024L;
        var error = $"{name} exceeds {MaxPartMegabytes} MB";
        if (entry.Length > limit)
        {
            throw new InvalidDataException(error);
        }
        // the declared size may be wrong: count the uncompressed bytes
        // the part is copied into memory: protects against zip bombs with a wrong declared size
        var buffer = new MemoryStream();
        using (var source = entry.Open())
        {
            var chunk = new byte[81920];
            int count;
            while ((count = source.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + count > limit)
                {
                    throw new InvalidDataException(error);
                }
                buffer.Write(chunk, 0, count);
            }
        }
        buffer.Position = 0;
        return XmlReader.Create(buffer, Settings);
    }

    /// <summary>
    /// Collects the text flow of a document into sections: paragraphs with outline level 0-3 outside tables
    /// are headings, paragraphs in table cells stay on the row line
    /// </summary>
    private sealed class FlowBuilder
    {
        /// <summary>Open paragraph: its text and outline level (null: body text)</summary>
        private sealed class Paragraph
        {
            /// <summary>Text of the paragraph</summary>
            internal StringBuilder Text { get; } = new();
            /// <summary>Outline level 0-8, null for body text</summary>
            internal int? Level { get; set; }
        }

        /// <summary>Completed sections</summary>
        private readonly List<(string Heading, string Text)> sections = [];
        /// <summary>Text of the current section</summary>
        private readonly StringBuilder text = new();
        /// <summary>Open paragraphs: a table cell can contain paragraphs inside a paragraph</summary>
        private readonly Stack<Paragraph> paragraphs = new();
        /// <summary>Heading of the current section</summary>
        private string heading = StartHeading;
        /// <summary>Depth of nested tables</summary>
        private int tables;

        /// <summary>True while a paragraph is open</summary>
        internal bool InParagraph => paragraphs.Count > 0;

        /// <summary>Text of the open paragraph, else the section text</summary>
        private StringBuilder Target => InParagraph ? paragraphs.Peek().Text : text;

        /// <summary>Opens a paragraph, optionally with an outline level</summary>
        internal void StartParagraph(int? outlineLevel = null) =>
            paragraphs.Push(new Paragraph { Level = outlineLevel });

        /// <summary>Sets the outline level of the open paragraph</summary>
        internal void SetOutlineLevel(int level)
        {
            if (InParagraph)
            {
                paragraphs.Peek().Level = level;
            }
        }

        // line breaks in the source are layout, not structure
        /// <summary>Appends document text; line breaks and tabs become spaces</summary>
        internal void AppendText(string value) =>
            Target.Append(value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '));

        /// <summary>Appends a structural character (space, hyphen) as is</summary>
        internal void Append(string value) =>
            Target.Append(value);

        /// <summary>Manual line break: a space inside tables (the row stays one line), else a new line</summary>
        internal void LineBreak() =>
            Target.Append(tables > 0 ? ' ' : '\n');

        /// <summary>
        /// Closes the open paragraph: a heading level 0-3 outside tables starts a new section, other paragraphs
        /// are appended as a line (or a space inside tables and nested paragraphs)
        /// </summary>
        internal void EndParagraph()
        {
            if (!InParagraph)
            {
                return;
            }
            var paragraph = paragraphs.Pop();
            if (paragraph.Level is >= 0 and <= 3 && tables == 0 && !InParagraph)
            {
                var value = HtmlReader.CleanLines(paragraph.Text.ToString()).Replace('\n', ' ').Replace('\t', ' ');
                if (value.Length > 0)
                {
                    AddSection();
                    heading = value.Length > MaxHeadingLength ? value[..MaxHeadingLength] : value;
                    return;
                }
            }
            Target.Append(paragraph.Text).Append(tables > 0 || InParagraph ? ' ' : '\n');
        }

        /// <summary>Opens a table on a new line</summary>
        internal void StartTable()
        {
            tables++;
            Target.Append('\n');
        }

        /// <summary>Closes a table</summary>
        internal void EndTable()
        {
            if (tables > 0)
            {
                tables--;
            }
            Target.Append('\n');
        }

        /// <summary>Starts a cell: cells are separated by tabs</summary>
        internal void StartCell() =>
            Target.Append('\t');

        /// <summary>Ends a row: one line per row</summary>
        internal void EndRow() =>
            Target.Append('\n');

        /// <summary>Closes all open paragraphs and returns the sections</summary>
        internal List<(string Heading, string Text)> Finish()
        {
            while (InParagraph)
            {
                EndParagraph();
            }
            AddSection();
            return sections;
        }

        /// <summary>Adds the current section if it holds text</summary>
        private void AddSection()
        {
            var value = HtmlReader.CleanLines(text.ToString());
            text.Clear();
            if (value.Length > 0)
            {
                sections.Add((heading, value));
            }
        }
    }
}
