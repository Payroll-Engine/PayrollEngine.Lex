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

    private const int MaxHeadingLength = 200;
    private const int MaxStyleDepth = 10;

    /// <summary>Heading of the text before the first heading</summary>
    internal const string StartHeading = "(start of document)";

    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string CompatibilityNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

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
                    case "table-row":
                        row = new StringBuilder();
                        rowRepeat = Repeat(xml, "number-rows-repeated");
                        if (xml.IsEmptyElement)
                        {
                            EndRow();
                        }
                        break;
                    case "table-cell":
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
                        continue;
                }
            }
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
        var type = xml.GetAttribute("value-type", OfficeNs);
        var value = type switch
        {
            "float" or "percentage" or "currency" => Number(xml.GetAttribute("value", OfficeNs)),
            "date" => Date(xml.GetAttribute("date-value", OfficeNs)),
            "time" => Time(xml.GetAttribute("time-value", OfficeNs)),
            _ => null
        };
        var text = CellText(xml);
        // boolean values hold no text
        if (type == "boolean")
        {
            return null;
        }
        return value ?? (text.Length > 0 ? text : null);
    }

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

    private static string Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Number(number) : null;

    private static string Number(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Date(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? Number(date.ToOADate()) : null;

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
        var styles = StyleOutlineLevels(archive);
        using var xml = OpenPart(archive, "word/document.xml");
        var flow = new FlowBuilder();
        var inText = false;
        xml.Read();
        while (!xml.EOF)
        {
            if (xml.NodeType == XmlNodeType.Element)
            {
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
                        case "style":
                            id = xml.GetAttribute("type", WordNs) is null or "paragraph" ? xml.GetAttribute("styleId", WordNs) : null;
                            basedOn = null;
                            level = null;
                            break;
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
        private sealed class Paragraph
        {
            internal StringBuilder Text { get; } = new();
            internal int? Level { get; set; }
        }

        private readonly List<(string Heading, string Text)> sections = [];
        private readonly StringBuilder text = new();
        private readonly Stack<Paragraph> paragraphs = new();
        private string heading = StartHeading;
        private int tables;

        internal bool InParagraph => paragraphs.Count > 0;

        private StringBuilder Target => InParagraph ? paragraphs.Peek().Text : text;

        internal void StartParagraph(int? outlineLevel = null) =>
            paragraphs.Push(new Paragraph { Level = outlineLevel });

        internal void SetOutlineLevel(int level)
        {
            if (InParagraph)
            {
                paragraphs.Peek().Level = level;
            }
        }

        // line breaks in the source are layout, not structure
        internal void AppendText(string value) =>
            Target.Append(value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '));

        internal void Append(string value) =>
            Target.Append(value);

        internal void LineBreak() =>
            Target.Append(tables > 0 ? ' ' : '\n');

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

        internal void StartTable()
        {
            tables++;
            Target.Append('\n');
        }

        internal void EndTable()
        {
            if (tables > 0)
            {
                tables--;
            }
            Target.Append('\n');
        }

        internal void StartCell() =>
            Target.Append('\t');

        internal void EndRow() =>
            Target.Append('\n');

        internal List<(string Heading, string Text)> Finish()
        {
            while (InParagraph)
            {
                EndParagraph();
            }
            AddSection();
            return sections;
        }

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
