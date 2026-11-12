using System.Collections.Generic;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of the source verification of one manifest (<see cref="VerifySourceOperation"/>)
/// </summary>
public sealed class VerifySourceFileResult
{
    /// <summary>Full path of lex.json</summary>
    public string LexPath { get; internal init; }

    /// <summary>lex.json text as read, the base of the write step</summary>
    public string Json { get; internal init; }

    /// <summary>Manifest</summary>
    public Manifest Manifest { get; internal init; }

    /// <summary>Result per verified entry</summary>
    public List<SourceResult> Results { get; internal init; } = [];

    /// <summary>Evidence files in the .lex folder that back no entry</summary>
    public List<string> UnusedEvidence { get; internal init; } = [];

    /// <summary>Result of the write step, null when nothing was written (report only)</summary>
    public SourceWriteResult Write { get; internal set; }

    /// <summary>Number of entries with the verification state</summary>
    /// <param name="match">Verification state</param>
    public int Count(SourceMatch match) => Results.Count(r => r.Match == match);

    /// <summary>Entries with numbers or quote missing in the source (partial, not found, quote not found)</summary>
    public int Problems => Results.Count(r => r.Match is SourceMatch.Partial or SourceMatch.NotFound or SourceMatch.QuoteNotFound);

    /// <summary>Entries with ambiguous evidence</summary>
    public int Ambiguous => Count(SourceMatch.Ambiguous);

    /// <summary>Entries verified by file identity (text tables)</summary>
    public int FileIdentities => Results.Count(r => r.FileIdentity);

    /// <summary>Languages of the verification, null when no entry read number words (text tables only)</summary>
    public string Languages => Results.FirstOrDefault(r => r.Languages != null)?.Languages;
}
