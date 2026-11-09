using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup values of a text table with the line counters
/// </summary>
public sealed class TextTableResult
{
    /// <summary>Converted lookup values in line order</summary>
    public List<TextTableRow> Rows { get; } = [];

    /// <summary>Line format, detected from the lines until the first converted line</summary>
    public TextTableFormat Format { get; internal set; }

    /// <summary>Number of lines read</summary>
    public int Lines { get; internal set; }

    /// <summary>Lines skipped by a filter of the map</summary>
    public int Filtered { get; internal set; }

    /// <summary>Lines without key or without value (e.g. header and trailer records without filter)</summary>
    public int Ignored => IgnoredLines.Count;

    /// <summary>Text of the ignored lines</summary>
    public List<string> IgnoredLines { get; } = [];
}
