using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Fields of an existing entry to change (<see cref="ManifestEditor.UpdateEntry"/>); a property set to null
/// stays as it is
/// </summary>
public sealed class EntryUpdate
{
    /// <summary>New inline content (not for an entry with a content file)</summary>
    public string Content { get; init; }

    /// <summary>New legal norm reference</summary>
    public string LegalReference { get; init; }

    /// <summary>New source URLs, main source first; replaces the list</summary>
    public List<string> SourceUrls { get; init; }

    /// <summary>New source quote; an empty text removes the quote</summary>
    public string SourceQuote { get; init; }

    /// <summary>New reference year (1990 to 2100)</summary>
    public int? ReferenceYear { get; init; }

    /// <summary>Remove the reference year (not together with <see cref="ReferenceYear"/>)</summary>
    public bool RemoveReferenceYear { get; init; }

    /// <summary>New description; an empty text removes the description</summary>
    public string Description { get; init; }
}
