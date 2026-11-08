using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of applying a lex manifest to its data regulation satellite
/// </summary>
public sealed class ApplyResult
{
    /// <summary>Full path of lex.json</summary>
    public string LexPath { get; init; }

    /// <summary>Name of the data regulation satellite folder</summary>
    public string Satellite { get; init; }

    /// <summary>Changes of the regulation values</summary>
    public List<ApplyChange> Changes { get; } = [];

    /// <summary>Lex entries that could not be applied, with reason</summary>
    public List<string> Skipped { get; } = [];

    /// <summary>Regulation values equal to the lex content</summary>
    public int Unchanged { get; internal set; }

    /// <summary>Regulation files written</summary>
    public List<string> WrittenFiles { get; } = [];

    /// <summary>Information without changes (e.g. no lookups in the regulation JSON)</summary>
    public string Note { get; internal set; }

    /// <summary>Error that prevented the apply action</summary>
    public string Error { get; set; }
}
