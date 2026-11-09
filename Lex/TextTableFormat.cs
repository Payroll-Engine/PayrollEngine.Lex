namespace PayrollEngine.Lex;

/// <summary>
/// Line format of a text table
/// </summary>
public enum TextTableFormat
{
    /// <summary>Fixed columns: start is a character index, length is required</summary>
    FixedColumns,

    /// <summary>Tab-delimited columns: start is the column index</summary>
    TabDelimited
}
