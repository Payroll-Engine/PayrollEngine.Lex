using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of a lex check of one data regulation satellite
/// </summary>
public sealed class CheckResult
{
    /// <summary>Full path of lex.json</summary>
    public string LexPath { get; init; }

    /// <summary>Name of the data regulation satellite folder</summary>
    public string Satellite { get; init; }
    internal string Regulation { get; set; }

    // manifest
    /// <summary>Structural errors of the manifest</summary>
    public List<string> ValidationErrors { get; } = [];

    /// <summary>snapshot.json exists next to lex.json</summary>
    public bool HasSnapshot { get; internal set; }

    // coverage
    internal int RegulationLookups { get; set; }

    /// <summary>Number of regulation lookup values</summary>
    public int RegulationValues { get; internal set; }

    /// <summary>Number of lex entries</summary>
    public int LexEntries { get; internal set; }
    internal int CoveredDirect { get; set; }
    internal int CoveredByLookup { get; set; }

    /// <summary>Regulation values without lex entry</summary>
    public int Uncovered { get; internal set; }

    /// <summary>Lex entries without regulation value</summary>
    public int Orphans { get; internal set; }

    /// <summary>Lex entries with a property path that does not resolve in the regulation value</summary>
    public int UnresolvedSplit { get; internal set; }

    /// <summary>Samples of regulation values without lex entry</summary>
    public List<string> UncoveredSamples { get; } = [];

    /// <summary>Samples of lex entries without regulation value</summary>
    public List<string> OrphanSamples { get; } = [];

    // value comparison
    internal int Compared { get; set; }

    /// <summary>Value differences between lex and regulation</summary>
    public int Drift { get; internal set; }

    /// <summary>Samples of value differences</summary>
    public List<string> DriftSamples { get; } = [];

    // traceability
    /// <summary>Lex entries without sourceUrls</summary>
    public int MissingSourceUrls { get; internal set; }

    /// <summary>Lex entries without sourceLocation</summary>
    public int MissingSourceLocation { get; internal set; }

    // source URLs (only with CheckUrls)
    internal bool UrlsChecked { get; set; }

    /// <summary>Source URLs that do not exist</summary>
    public List<string> DeadUrls { get; } = [];
    internal int BlockedUrls { get; set; }

    /// <summary>Source URLs whose normalized text differs from the snapshot: check the values</summary>
    public List<string> ChangedSources { get; } = [];
    internal int SourcesCompared { get; set; }
    internal int SourcesNotComparable { get; set; }
    internal int SourcesWithoutBaseline { get; set; }

    /// <summary>A missing sourceLocation is a finding only in the mode Required</summary>
    public bool RequireLocation { get; internal set; }

    /// <summary>Lex entries whose sourceLocation is not entry-specific: the same long location on many entries
    /// or an overlong location (a finding only in the mode Required)</summary>
    public int UnspecificLocations { get; internal set; }

    /// <summary>Samples of lex entries with a location that is not entry-specific</summary>
    public List<string> UnspecificLocationSamples { get; } = [];

    /// <summary>Lex entries whose main source (first source URL) is a secondary source (official: false);
    /// a finding only in the release mode</summary>
    public int SecondaryMainSource { get; internal set; }

    /// <summary>Samples of lex entries with a secondary main source</summary>
    public List<string> SecondaryMainSourceSamples { get; } = [];

    // documented exclusions
    internal int ExcludedCoverage { get; set; }
    internal int ExcludedSourceLocation { get; set; }
    internal int ExcludedOrphans { get; set; }
    internal int ExcludedAutoVerification { get; set; }

    /// <summary>Entry properties documented as derived (exclusion kind derived)</summary>
    public int ExcludedDerived { get; internal set; }

    /// <summary>Zero entry properties documented as not applicable (exclusion kind notApplicable)</summary>
    public int ExcludedNotApplicable { get; internal set; }
    internal List<string> ExclusionNotes { get; } = [];

    /// <summary>Bundle entries with zero properties without justification (neither notApplicable nor derived);
    /// a finding only in the release mode</summary>
    public int UnjustifiedZero { get; internal set; }

    /// <summary>Samples of bundle entries with zero properties without justification</summary>
    public List<string> UnjustifiedZeroSamples { get; } = [];

    /// <summary>Entries with a reference year other than the validFrom year (information)</summary>
    public int OtherReferenceYear { get; internal set; }

    /// <summary>Samples of entries with a reference year other than the validFrom year</summary>
    public List<string> OtherReferenceYearSamples { get; } = [];

    /// <summary>Evidence files that back no entry (see <see cref="Checker.UnusedEvidence"/>); a finding only in the release mode</summary>
    public int UnusedEvidence { get; internal set; }

    /// <summary>Samples of evidence files that back no entry</summary>
    public List<string> UnusedEvidenceSamples { get; } = [];

    /// <summary>Lex entries without legalReference</summary>
    public int MissingLegalReference { get; internal set; }

    // verification
    /// <summary>Lex entries with an automatic verification record</summary>
    public int VerifiedAuto { get; internal set; }

    /// <summary>Lex entries with a manual verification record</summary>
    public int VerifiedManual { get; internal set; }

    /// <summary>Lex entries without a valid verification record</summary>
    public int Unverified { get; internal set; }

    /// <summary>Samples of lex entries without verification record</summary>
    public List<string> UnverifiedSamples { get; } = [];

    /// <summary>An entry without verification record is a finding only in the mode Required</summary>
    public bool RequireVerification { get; internal set; }

    /// <summary>Open lex entries (previous cycle value without new verification), always a finding</summary>
    public int Open { get; internal set; }

    /// <summary>Samples of open lex entries</summary>
    public List<string> OpenSamples { get; } = [];

    /// <summary>Manual verifications of a four-eyes source without second review (reviewedBy, reviewedOn)</summary>
    public int MissingReview { get; internal set; }

    /// <summary>Samples of manual verifications without second review</summary>
    public List<string> MissingReviewSamples { get; } = [];

    // release gate
    /// <summary>Release mode: location and verification required, a secondary main source and an entry
    /// that needs a manual verification are findings</summary>
    public bool Release { get; internal set; }

    /// <summary>The entries were verified against their local evidence (release mode)</summary>
    public bool SourcesVerified { get; internal set; }

    /// <summary>Entries without manual verification whose numbers are found with weak evidence only (ambiguous)</summary>
    public int Ambiguous { get; internal set; }

    /// <summary>Entries without manual verification whose sourceQuote is not found in the source</summary>
    public int QuoteNotFound { get; internal set; }

    /// <summary>Entries excluded from automatic verification with an automatic verification record</summary>
    public int ExcludedAutoVerified { get; internal set; }

    /// <summary>Samples of entries that need a manual verification (ambiguous, quote not found, excluded from auto verification)</summary>
    public List<string> ManualVerificationSamples { get; } = [];

    /// <summary>Entries that need a manual verification</summary>
    public int ManualVerificationRequired => Ambiguous + QuoteNotFound + ExcludedAutoVerified;

    /// <summary>Entries without manual verification that can be verified neither locally nor live: no readable local
    /// evidence and no source URL that is readable live according to the snapshot (release mode)</summary>
    public int NotVerifiable { get; internal set; }

    /// <summary>Samples of entries that can be verified neither locally nor live</summary>
    public List<string> NotVerifiableSamples { get; } = [];

    // evidence
    /// <summary>Problems of the evidence files</summary>
    public List<string> FileProblems { get; } = [];

    // validFrom
    /// <summary>validFrom of the manifest</summary>
    public string LexValidFrom { get; internal set; }

    /// <summary>validFrom of the regulation</summary>
    public string RegulationValidFrom { get; internal set; }

    /// <summary>validFrom of manifest and regulation match</summary>
    public bool ValidFromMatch { get; internal set; } = true;

    // information
    /// <summary>Information that needs a manual review</summary>
    public string Note { get; internal set; }

    /// <summary>Error that prevented the check</summary>
    public string Error { get; set; }

    /// <summary>
    /// Any finding that needs action
    /// </summary>
    public bool HasFindings =>
        Error != null || ValidationErrors.Count > 0 || !HasSnapshot || Uncovered > 0 || Orphans > 0 ||
        UnresolvedSplit > 0 || Drift > 0 || MissingSourceUrls > 0 || (RequireLocation && MissingSourceLocation > 0) ||
        MissingLegalReference > 0 || FileProblems.Count > 0 || DeadUrls.Count > 0 || ChangedSources.Count > 0 ||
        (RequireVerification && Unverified > 0) || Open > 0 || MissingReview > 0 || !ValidFromMatch || Note != null ||
        (RequireLocation && UnspecificLocations > 0) ||
        (Release && (SecondaryMainSource > 0 || ManualVerificationRequired > 0 || UnjustifiedZero > 0 || UnusedEvidence > 0 ||
                     NotVerifiable > 0));
}
