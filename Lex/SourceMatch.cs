
// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Verification state of one lex entry against its source
/// </summary>
public enum SourceMatch
{
    /// <summary>All numbers of the entry are found in the source</summary>
    Found,

    /// <summary>All numbers are found, but the evidence is weak: the entry holds a single number that is written
    /// with two digits at most (22%, 1.5), is written with three digits at most and occurs in many sections,
    /// or is found in words only; or a number of the entry has a hit closest to another year and no hit closest
    /// to the relevant year (reference year of the entry or year of validFrom; e.g. a table over several years).
    /// Confirm the value manually (LexAttest) or anchor it with a sourceQuote.</summary>
    Ambiguous,

    /// <summary>Some numbers are found, others are not</summary>
    Partial,

    /// <summary>None of the numbers is found</summary>
    NotFound,

    /// <summary>The sourceQuote of the entry is not found in any readable source</summary>
    QuoteNotFound,

    /// <summary>No source can be read (blocked host, script-only page, scanned PDF)</summary>
    Unreadable,

    /// <summary>The entry holds no numbers to look for</summary>
    NoNumbers,

    /// <summary>The content of the entry cannot be read (content file missing or not JSON)</summary>
    NoContent,

    /// <summary>The entry has no local evidence file for any source (local verification)</summary>
    NoEvidence
}
