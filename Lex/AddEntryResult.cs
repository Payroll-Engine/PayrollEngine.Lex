using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of adding an entry to a manifest (<see cref="AddEntryOperation"/>)
/// </summary>
public sealed class AddEntryResult
{
    /// <summary>Full path of lex.json, null when no manifest was found</summary>
    public string LexPath { get; internal init; }

    /// <summary>Errors that prevented the entry; nothing written</summary>
    public List<string> Errors { get; internal init; } = [];

    /// <summary>Required fields written empty, reported by the check until they are filled
    /// (content, sourceUrls, sourceLocation, legalReference)</summary>
    public List<string> Missing { get; internal init; } = [];

    /// <summary>The entry was added and the manifest saved</summary>
    public bool Added => Errors.Count == 0;
}
