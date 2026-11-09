using System.Collections.Generic;
using System.Linq;

// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Result for one entry
/// </summary>
public sealed class SourceResult
{
    /// <summary>Key of the lex entry</summary>
    public string Key { get; internal init; }

    /// <summary>Source URLs of the lex entry</summary>
    public List<string> Urls { get; internal init; } = [];

    /// <summary>Verification state</summary>
    public SourceMatch Match { get; internal init; }

    /// <summary>Numbers of the entry that were looked up (a sample of a large table)</summary>
    public int NumbersTotal { get; internal init; }

    /// <summary>All numbers of the entry content; more than <see cref="NumbersTotal"/> when sampled</summary>
    public int ContentNumbers { get; internal init; }

    /// <summary>The numbers of a large table were sampled</summary>
    public bool Sampled => ContentNumbers > NumbersTotal;

    /// <summary>Numbers found in the source</summary>
    public int NumbersFound { get; internal init; }

    /// <summary>Numbers missing in the source</summary>
    public List<string> Missing { get; internal init; } = [];

    /// <summary>Proposed source location (sections that hold the numbers)</summary>
    public string Location { get; internal init; }

    /// <summary>Reason of an unreadable source; with several sources, the unreadable ones; reason of missing content</summary>
    public string Reason { get; internal init; }

    /// <summary>Why the evidence of an ambiguous entry is weak</summary>
    public string Ambiguity { get; internal init; }

    /// <summary>Source quote of the entry</summary>
    public string Quote { get; internal init; }

    /// <summary>Local evidence file of the entry (first source with a local file), null for a live verification</summary>
    public string Evidence { get; internal set; }

    /// <summary>Languages of the verification with the hash of their profiles (<see cref="LanguageSet.Label"/>)</summary>
    public string Languages { get; internal set; }

    /// <summary>Text table verified by file identity: the table is the authority file declared with its source URLs;
    /// no numbers are looked up (see <see cref="ManifestVerifier.FileIdentityVerification"/>)</summary>
    public bool FileIdentity { get; internal init; }

    /// <summary>Reason of the autoVerification exclusion that matches the entry, null when not excluded</summary>
    public string ExcludedReason { get; internal set; }

    /// <summary>Derived properties of the entry (exclusion kind derived); their numbers are not looked up</summary>
    public List<DerivedValue> Derived { get; internal init; } = [];

    /// <summary>Property paths of a bundle entry with the value 0; their numbers are not looked up</summary>
    public List<string> Zero { get; internal init; } = [];

    /// <summary>Zero properties of the entry with a notApplicable exclusion</summary>
    public List<DerivedValue> NotApplicable { get; internal init; } = [];

    /// <summary>Zero properties without justification: neither a notApplicable nor a derived exclusion</summary>
    public List<string> UnjustifiedZero =>
        DerivedProperties.Unjustified(Zero, NotApplicable.Concat(Derived).Select(x => x.Property));

    /// <summary>Hint for the review of the result, e.g. all numbers of the entry are derived</summary>
    public string Note { get; internal init; }

    /// <summary>The source location and the automatic verification record may be written</summary>
    public bool CanWrite => Match == SourceMatch.Found && ExcludedReason == null;
}
