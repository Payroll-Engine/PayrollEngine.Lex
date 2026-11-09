namespace PayrollEngine.Lex;

/// <summary>
/// Difference between the rows of a text table and the values of a regulation lookup
/// </summary>
internal sealed class TableDifference
{
    /// <summary>Rows without regulation value</summary>
    internal int Added { get; set; }

    /// <summary>Regulation values without row</summary>
    internal int Removed { get; set; }

    /// <summary>Rows with a different regulation value</summary>
    internal int Changed { get; set; }

    /// <summary>Rows equal to their regulation value</summary>
    internal int Unchanged { get; set; }

    /// <summary>The lookup values differ from the table</summary>
    internal bool HasDifferences => Added + Removed + Changed > 0;

    /// <summary>Counts for reports, e.g. +3 added, -0 removed, ~1 changed</summary>
    internal string Summary => $"+{Added} added, -{Removed} removed, ~{Changed} changed";
}
