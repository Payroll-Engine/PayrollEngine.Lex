namespace PayrollEngine.Lex;

/// <summary>
/// Line of a rolled file that still mentions the previous year
/// </summary>
public sealed class YearMention
{
    /// <summary>File path relative to the new satellite</summary>
    public string File { get; init; }

    /// <summary>Line number (1-based)</summary>
    public int Line { get; init; }

    /// <summary>Line text (trimmed, shortened)</summary>
    public string Text { get; init; }
}
