using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of a rollover of one data regulation satellite
/// </summary>
public sealed class RolloverResult
{
    /// <summary>Directory of the previous cycle</summary>
    public string SourceDirectory { get; init; }

    /// <summary>Directory of the new cycle</summary>
    public string TargetDirectory { get; internal set; }

    /// <summary>validFrom of the previous cycle (lex.json)</summary>
    public string PreviousValidFrom { get; internal set; }

    /// <summary>validFrom of the new cycle (cycle start)</summary>
    public string ValidFrom { get; init; }

    /// <summary>Files of the new satellite, relative paths</summary>
    public List<string> Files { get; } = [];

    /// <summary>Renamed files and folders (previous name -> new name)</summary>
    public List<string> Renamed { get; } = [];

    /// <summary>Changed files with the kind of change</summary>
    public List<string> Changed { get; } = [];

    /// <summary>Lex entries marked as open</summary>
    public int OpenEntries { get; internal set; }

    /// <summary>Lines that still mention the previous year: review them</summary>
    public List<YearMention> YearMentions { get; } = [];

    /// <summary>Information for the follow-up work</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Error that prevented the rollover, nothing written</summary>
    public string Error { get; internal set; }
}
