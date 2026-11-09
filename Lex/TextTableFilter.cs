using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Line filter of a text table: the column value must be one of the values (e.g. a record type)
/// </summary>
public sealed class TextTableFilter
{
    /// <summary>
    /// 0-based character index of a fixed column, column index of a tab-delimited line
    /// </summary>
    public int Start { get; set; }

    /// <summary>
    /// Character count of a fixed column (required), ignored for tab-delimited lines
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Accepted column values, compared with the trimmed column text (case-sensitive)
    /// </summary>
    public List<string> Values { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"[{Start}-{Length}] in ({string.Join(", ", Values ?? [])})";
}
