using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

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

/// <summary>
/// Checks a lex manifest against the lookup values of its data regulation satellite:
/// coverage, orphans, value differences, traceability fields, evidence files and validFrom.
/// </summary>
public static class Checker
{
    private const int MaxSamples = 8;

    /// <summary>Entries with this key prefix reference an official calculator and have no regulation value</summary>
    private const string ReferenceEntryPrefix = "OfficialCalculator.";

    /// <summary>Entries with this key prefix reference an official calculator and have no regulation value</summary>
    internal static bool IsReferenceEntry(string key) =>
        key.StartsWith(ReferenceEntryPrefix, StringComparison.Ordinal);

    internal static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions SnapshotOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Check one satellite
    /// </summary>
    /// <param name="manifest">Manifest (lex.json)</param>
    /// <param name="lexPath">Full path of lex.json</param>
    /// <param name="requireLocation">Treat a missing sourceLocation as a finding</param>
    /// <param name="requireVerification">Treat an entry without verification record as a finding</param>
    /// <param name="release">Release gate: includes <paramref name="requireLocation"/> and <paramref name="requireVerification"/>,
    /// a secondary main source, an entry excluded from auto verification with an automatic record and a zero property
    /// without notApplicable exclusion are findings.
    /// The entries that need a manual verification are found by <see cref="CheckSources"/>.</param>
    public static CheckResult Check(Manifest manifest, string lexPath, bool requireLocation = false,
        bool requireVerification = false, bool release = false)
    {
        var lexDirectory = Path.GetDirectoryName(lexPath)!;
        var satelliteDirectory = Directory.GetParent(lexDirectory)?.FullName ?? lexDirectory;
        var result = new CheckResult
        {
            LexPath = lexPath,
            Satellite = Path.GetFileName(satelliteDirectory),
            RequireLocation = requireLocation || release,
            RequireVerification = requireVerification || release,
            Release = release,
            LexEntries = manifest.Values.Count,
            LexValidFrom = manifest.ValidFrom,
            HasSnapshot = File.Exists(Path.Combine(lexDirectory, "snapshot.json"))
        };

        // manifest structure (required fields, duplicates, content/contentFile, files)
        result.ValidationErrors.AddRange(Validator.Validate(manifest, lexDirectory));
        CheckVerification(manifest, result);

        // regulation values
        RegulationData regulation;
        try
        {
            regulation = RegulationReader.Read(satelliteDirectory);
        }
        catch (Exception exception)
        {
            result.Error = "Regulation JSON could not be read: " + exception.Message;
            return result;
        }
        var values = regulation.Values;
        result.Regulation = regulation.Name;
        result.RegulationValidFrom = regulation.ValidFrom;
        result.RegulationLookups = regulation.LookupCount;
        result.RegulationValues = values.Count;

        CheckTraceability(manifest, result);
        CheckValidFrom(result);
        CheckCoverage(manifest, lexDirectory, result, regulation);
        CheckPropertyExclusions(manifest, lexDirectory, result);
        CheckZeros(manifest, lexDirectory, result);
        CheckReferenceYears(manifest, result);
        CheckEvidence(manifest, lexDirectory, result);
        foreach (var exclusion in manifest.Exclusions)
        {
            result.ExclusionNotes.Add($"{exclusion.Kind} `{exclusion.Pattern}`: {exclusion.Reason}");
        }

        if (values.Count == 0 && manifest.Values.Count > 0)
        {
            // lookup values live outside the Regulation folder: no mapping possible
            result.Note = $"The regulation JSON holds no lookup values; {manifest.Values.Count} lex entries could not be mapped (manual review)";
            result.Orphans = 0;
            result.OrphanSamples.Clear();
        }
        return result;
    }

    /// <summary>
    /// Release gate: verify the entries without manual verification against their local evidence
    /// (<see cref="SourceVerifier.VerifyManifest"/>); an ambiguous entry and an entry whose quote is not found
    /// need a manual verification; an entry without readable local evidence whose sources are not readable live
    /// according to the snapshot (live sources with text hash) can be verified neither locally nor live
    /// </summary>
    /// <param name="manifest">Manifest (lex.json)</param>
    /// <param name="result">Result of <see cref="Check"/></param>
    public static async Task CheckSources(Manifest manifest, CheckResult result)
    {
        var manual = manifest.Values
            .Where(x => x.Key != null && x.Verification?.Mode == Verification.ModeManual)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.Ordinal);
        var readableLive = UrlChecker.ReadBaseline(result.LexPath);
        var sources = await SourceVerifier.VerifyManifest(manifest, Path.GetDirectoryName(result.LexPath)!);
        foreach (var source in sources.Where(x => !manual.Contains(x.Key)))
        {
            switch (source.Match)
            {
                case SourceMatch.NoEvidence or SourceMatch.Unreadable when !source.Urls.Any(readableLive.ContainsKey):
                    result.NotVerifiable++;
                    AddSample(result.NotVerifiableSamples, source.Match == SourceMatch.NoEvidence
                        ? $"{source.Key} (no local evidence)"
                        : $"{source.Key} (local evidence not readable: {source.Reason})");
                    break;
                case SourceMatch.Ambiguous:
                    result.Ambiguous++;
                    AddSample(result.ManualVerificationSamples, $"{source.Key} (ambiguous: {source.Ambiguity})");
                    break;
                case SourceMatch.QuoteNotFound:
                    result.QuoteNotFound++;
                    AddSample(result.ManualVerificationSamples, $"{source.Key} (quote not found)");
                    break;
            }
        }
        result.SourcesVerified = true;
    }

    private const int SharedLocationEntries = 5;
    private const int SharedLocationLength = 200;
    internal const int MaxLocationLength = 500;

    private static void CheckTraceability(Manifest manifest, CheckResult result)
    {
        var exclusions = manifest.Exclusions.Where(x => x.Kind == Exclusion.KindSourceLocation).ToList();
        var used = new HashSet<Exclusion>();
        var secondary = manifest.Sources
            .Where(x => !x.IsOfficial && !string.IsNullOrWhiteSpace(x.Url))
            .Select(x => x.Url)
            .ToHashSet(StringComparer.Ordinal);
        var shared = manifest.Values
            .Select(x => x.SourceLocation?.Trim())
            .Where(x => x is { Length: > SharedLocationLength })
            .GroupBy(x => x, StringComparer.Ordinal)
            .Where(g => g.Count() >= SharedLocationEntries)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var value in manifest.Values)
        {
            if (!value.Urls.Any()) result.MissingSourceUrls++;
            var mainSource = value.Urls.FirstOrDefault();
            if (mainSource != null && secondary.Contains(mainSource))
            {
                result.SecondaryMainSource++;
                AddSample(result.SecondaryMainSourceSamples, $"{value.Key}: {mainSource}");
            }
            var location = value.SourceLocation?.Trim();
            if (location != null && (location.Length > MaxLocationLength || shared.Contains(location)))
            {
                result.UnspecificLocations++;
                AddSample(result.UnspecificLocationSamples, value.Key);
            }
            if (string.IsNullOrWhiteSpace(value.SourceLocation))
            {
                var exclusion = exclusions.FirstOrDefault(x => x.Matches(value.Key));
                if (exclusion != null)
                {
                    used.Add(exclusion);
                    result.ExcludedSourceLocation++;
                }
                else
                {
                    result.MissingSourceLocation++;
                }
            }
            if (string.IsNullOrWhiteSpace(value.LegalReference)) result.MissingLegalReference++;
        }
        foreach (var exclusion in exclusions.Where(x => !used.Contains(x)))
        {
            result.ValidationErrors.Add($"Exclusion (sourceLocation) '{exclusion.Pattern}' matches no entry without sourceLocation: remove it");
        }
    }

    private static void CheckVerification(Manifest manifest, CheckResult result)
    {
        var fourEyesUrls = manifest.Sources
            .Where(x => x.IsFourEyes && !string.IsNullOrWhiteSpace(x.Url))
            .Select(x => x.Url)
            .ToHashSet(StringComparer.Ordinal);
        var autoExclusions = manifest.Exclusions.Where(x => x.Kind == Exclusion.KindAutoVerification).ToList();
        var usedExclusions = new HashSet<Exclusion>();
        foreach (var value in manifest.Values)
        {
            var autoExclusion = value.Key != null ? autoExclusions.FirstOrDefault(x => x.Matches(value.Key)) : null;
            if (autoExclusion != null)
            {
                usedExclusions.Add(autoExclusion);
                result.ExcludedAutoVerification++;
            }
            if (value.Open)
            {
                result.Open++;
                AddSample(result.OpenSamples, value.Key);
                continue;
            }
            var verification = value.Verification;
            switch (verification?.Mode)
            {
                case Verification.ModeAuto:
                    result.VerifiedAuto++;
                    if (autoExclusion != null)
                    {
                        result.ExcludedAutoVerified++;
                        AddSample(result.ManualVerificationSamples, $"{value.Key} (excluded from auto verification)");
                    }
                    break;
                case Verification.ModeManual:
                    result.VerifiedManual++;
                    if (value.Urls.Any(fourEyesUrls.Contains) &&
                        (string.IsNullOrWhiteSpace(verification.ReviewedBy) || string.IsNullOrWhiteSpace(verification.ReviewedOn)))
                    {
                        result.MissingReview++;
                        AddSample(result.MissingReviewSamples, value.Key);
                    }
                    break;
                default:
                    result.Unverified++;
                    AddSample(result.UnverifiedSamples, value.Key);
                    break;
            }
        }
        foreach (var exclusion in autoExclusions.Where(x => !usedExclusions.Contains(x)))
        {
            result.ValidationErrors.Add($"Exclusion (autoVerification) '{exclusion.Pattern}' matches no entry: remove it");
        }
    }

    private static void CheckValidFrom(CheckResult result)
    {
        if (result.LexValidFrom == null || result.RegulationValidFrom == null)
        {
            return;
        }
        result.ValidFromMatch = string.Equals(
            Left10(result.LexValidFrom), Left10(result.RegulationValidFrom), StringComparison.Ordinal);
    }

    private static string Left10(string value) => value.Length >= 10 ? value[..10] : value;

    private static void CheckCoverage(Manifest manifest, string lexDirectory, CheckResult result, RegulationData regulation)
    {
        var values = regulation.Values;
        var index = new RegulationIndex(regulation);
        var editionTag = EntryMapping.EditionTag(manifest);

        var coverageExclusions = manifest.Exclusions.Where(x => x.Kind == Exclusion.KindCoverage).ToList();
        var orphanExclusions = manifest.Exclusions.Where(x => x.Kind == Exclusion.KindOrphan).ToList();
        var usedExclusions = new HashSet<Exclusion>();
        var coveredLookups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Values)
        {
            if (string.IsNullOrEmpty(entry.Key))
            {
                continue;
            }
            var target = EntryMapping.Resolve(entry, index, editionTag);
            if (target.Kind == EntryTargetKind.Values)
            {
                var content = CompareContent(entry, lexDirectory);
                foreach (var value in target.Values)
                {
                    value.Covered = true;
                    if (content != null)
                    {
                        Compare(result, entry.Key, content, value, target.Remainder);
                    }
                }
                continue;
            }
            // lookup-level coverage is reserved for entries backed by a content file (e.g. a whole table)
            if (target.Kind == EntryTargetKind.Lookup)
            {
                coveredLookups.Add(target.BaseKey);
                var content = CompareContent(entry, lexDirectory);
                if (content != null && target.Lookup.Values.Count == 1)
                {
                    Compare(result, entry.Key, content, target.Lookup.Values[0], string.Empty);
                }
                continue;
            }
            if (IsReferenceEntry(entry.Key))
            {
                // reference entry (link to an official calculator), not a regulation value
                continue;
            }
            var orphanExclusion = orphanExclusions.FirstOrDefault(x => x.Matches(entry.Key));
            if (orphanExclusion != null)
            {
                usedExclusions.Add(orphanExclusion);
                result.ExcludedOrphans++;
                continue;
            }
            result.Orphans++;
            AddSample(result.OrphanSamples, entry.Key);
        }

        foreach (var value in values)
        {
            if (value.Covered)
            {
                result.CoveredDirect++;
            }
            else if (coveredLookups.Contains(value.Lookup) ||
                     (value.LookupAlias != null && coveredLookups.Contains(value.LookupAlias)))
            {
                result.CoveredByLookup++;
            }
            else
            {
                var fullKey = value.Lookup + "." + value.Key;
                var exclusion = coverageExclusions.FirstOrDefault(x =>
                    x.Matches(fullKey) || (value.LookupAlias != null && x.Matches(value.LookupAlias + "." + value.Key)));
                if (exclusion != null)
                {
                    usedExclusions.Add(exclusion);
                    result.ExcludedCoverage++;
                    continue;
                }
                result.Uncovered++;
                AddSample(result.UncoveredSamples, fullKey);
            }
        }
        foreach (var exclusion in coverageExclusions.Where(x => !usedExclusions.Contains(x)))
        {
            result.ValidationErrors.Add($"Exclusion (coverage) '{exclusion.Pattern}' matches no uncovered value: remove it");
        }
        foreach (var exclusion in orphanExclusions.Where(x => !usedExclusions.Contains(x)))
        {
            result.ValidationErrors.Add($"Exclusion (orphan) '{exclusion.Pattern}' matches no lex entry without regulation value: remove it");
        }
    }

    /// <summary>
    /// A derived or notApplicable exclusion must address an existing property of an entry (e.g. not renamed);
    /// a notApplicable property must have the value 0
    /// </summary>
    private static void CheckPropertyExclusions(Manifest manifest, string lexDirectory, CheckResult result)
    {
        var keys = DerivedProperties.Keys(manifest);
        foreach (var exclusion in manifest.Exclusions.Where(x => DerivedProperties.IsPropertyKind(x.Kind)))
        {
            var (key, path) = DerivedProperties.Resolve(exclusion.Pattern, keys);
            if (key == null)
            {
                result.ValidationErrors.Add($"Exclusion ({exclusion.Kind}) '{exclusion.Pattern}' matches no entry: remove or rename it");
                continue;
            }
            if (path.Length == 0)
            {
                // whole entry: reported by the validator
                continue;
            }
            var entry = manifest.Values.First(x => string.Equals(x.Key, key, StringComparison.Ordinal));
            if (!DerivedProperties.TryGetValue(CompareContent(entry, lexDirectory), path, out var value))
            {
                result.ValidationErrors.Add(
                    $"Exclusion ({exclusion.Kind}) '{exclusion.Pattern}' matches no property '{path}' of entry '{key}': remove or rename it");
                continue;
            }
            if (exclusion.Kind == Exclusion.KindDerived)
            {
                result.ExcludedDerived++;
                continue;
            }
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || number != 0m)
            {
                result.ValidationErrors.Add(
                    $"Exclusion (notApplicable) '{exclusion.Pattern}': property '{path}' of entry '{key}' is {Shorten(value)}, not 0: remove it");
                continue;
            }
            result.ExcludedNotApplicable++;
        }
    }

    /// <summary>
    /// Entries whose reference year differs from the validFrom year: listed as information for the review
    /// </summary>
    private static void CheckReferenceYears(Manifest manifest, CheckResult result)
    {
        var validYear = manifest.ValidFrom is { Length: >= 4 } &&
                        int.TryParse(manifest.ValidFrom.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : (int?)null;
        foreach (var entry in manifest.Values.Where(x => x.ReferenceYear != null && x.ReferenceYear != validYear))
        {
            result.OtherReferenceYear++;
            AddSample(result.OtherReferenceYearSamples, $"{entry.Key} ({entry.ReferenceYear})");
        }
    }

    /// <summary>
    /// Zero properties of a bundle entry need a notApplicable exclusion (or a derived exclusion): a rate set to 0
    /// by mistake would otherwise never be looked up in the source
    /// </summary>
    private static void CheckZeros(Manifest manifest, string lexDirectory, CheckResult result)
    {
        var keys = DerivedProperties.Keys(manifest);
        foreach (var entry in manifest.Values)
        {
            if (string.IsNullOrEmpty(entry.Key) || IsReferenceEntry(entry.Key))
            {
                continue;
            }
            var zeros = DerivedProperties.Zeros(CompareContent(entry, lexDirectory));
            if (zeros.Count == 0)
            {
                continue;
            }
            var justified = DerivedProperties.Of(entry.Key, manifest.Exclusions, keys)
                .Select(x => DerivedProperties.Resolve(x.Pattern, keys).Path);
            var unjustified = DerivedProperties.Unjustified(zeros, justified);
            if (unjustified.Count == 0)
            {
                continue;
            }
            result.UnjustifiedZero++;
            AddSample(result.UnjustifiedZeroSamples, $"{entry.Key} ({string.Join(", ", unjustified)})");
        }
    }

    private static void CheckEvidence(Manifest manifest, string lexDirectory, CheckResult result)
    {
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.FileName))
            {
                continue;
            }
            declared.Add(file.FileName);
            var path = Path.Combine(lexDirectory, file.FileName);
            if (!File.Exists(path))
            {
                result.FileProblems.Add($"declared file not in .lex: {file.FileName}");
            }
            else
            {
                paths.Add(path);
            }
        }
        foreach (var value in manifest.Values)
        {
            if (!string.IsNullOrWhiteSpace(value.DocumentRef))
            {
                if (!declared.Contains(value.DocumentRef))
                {
                    result.FileProblems.Add($"documentRef not in files[]: {value.DocumentRef}");
                }
                else if (!File.Exists(Path.Combine(lexDirectory, value.DocumentRef)))
                {
                    result.FileProblems.Add($"documentRef file missing: {value.DocumentRef}");
                }
            }
            if (value.HasContentFile)
            {
                var path = ContentHash.ResolvePath(lexDirectory, value.ContentFile);
                if (File.Exists(path))
                {
                    paths.Add(path);
                }
                else
                {
                    result.FileProblems.Add($"contentFile missing: {value.ContentFile}");
                }
            }
        }

        CheckVerificationEvidence(manifest, lexDirectory, result);
        var unused = UnusedEvidence(manifest, lexDirectory);
        result.UnusedEvidence = unused.Count;
        result.UnusedEvidenceSamples.AddRange(unused.Take(MaxSamples));

        // git state of the evidence: untracked or ignored files are not part of the compliance record
        paths.Add(Path.Combine(lexDirectory, "lex.json"));
        if (result.HasSnapshot)
        {
            paths.Add(Path.Combine(lexDirectory, "snapshot.json"));
        }
        var untracked = GitList(lexDirectory, false);
        var ignored = GitList(lexDirectory, true);
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ignored.Contains(path))
            {
                result.FileProblems.Add($"git-ignored: {Path.GetFileName(path)}");
            }
            else if (untracked.Contains(path))
            {
                result.FileProblems.Add($"untracked in git: {Path.GetFileName(path)}");
            }
        }

        var distinct = result.FileProblems.Distinct(StringComparer.Ordinal).Take(MaxSamples).ToList();
        result.FileProblems.Clear();
        result.FileProblems.AddRange(distinct);
    }

    /// <summary>
    /// Evidence files in the .lex folder that back no entry: a declared file is used by a documentRef, by a
    /// source URL of an entry, as contentFile or as evidence of a verification record. A file in the .lex folder
    /// without declaration in files[] is unused too (not frozen in the snapshot).
    /// An unused file is never read by LexVerifySource: its entries count as without local evidence.
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <returns>File names, undeclared files with the suffix " (not in files[])"</returns>
    public static List<string> UnusedEvidence(Manifest manifest, string lexDirectory)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        var contentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in manifest.Values)
        {
            if (!string.IsNullOrWhiteSpace(value.DocumentRef)) referenced.Add(value.DocumentRef);
            if (!string.IsNullOrWhiteSpace(value.Verification?.Evidence)) referenced.Add(value.Verification.Evidence);
            if (value.HasContentFile) contentFiles.Add(Path.GetFullPath(ContentHash.ResolvePath(lexDirectory, value.ContentFile)));
            urls.UnionWith(value.Urls);
        }

        var unused = new List<string>();
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files.Where(x => !string.IsNullOrWhiteSpace(x.FileName)))
        {
            declared.Add(file.FileName);
            if (referenced.Contains(file.FileName) || file.Urls.Any(urls.Contains) ||
                contentFiles.Contains(Path.GetFullPath(Path.Combine(lexDirectory, file.FileName))) ||
                !File.Exists(Path.Combine(lexDirectory, file.FileName)))
            {
                continue;
            }
            unused.Add(file.FileName);
        }
        if (Directory.Exists(lexDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(lexDirectory))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith('.') || declared.Contains(name) || contentFiles.Contains(Path.GetFullPath(path)) ||
                    string.Equals(name, "lex.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "snapshot.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                unused.Add(name + " (not in files[])");
            }
        }
        return unused;
    }

    /// <summary>
    /// The evidence of a verification record must be frozen in the snapshot
    /// </summary>
    private static void CheckVerificationEvidence(Manifest manifest, string lexDirectory, CheckResult result)
    {
        var evidences = manifest.Values
            .Select(x => x.Verification?.Evidence)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (evidences.Count == 0 || !result.HasSnapshot)
        {
            return;
        }

        Snapshot snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<Snapshot>(
                File.ReadAllText(Path.Combine(lexDirectory, "snapshot.json")), SnapshotOptions);
        }
        catch (Exception exception)
        {
            result.FileProblems.Add("snapshot.json could not be read: " + exception.Message);
            return;
        }
        var frozen = (snapshot?.Files ?? [])
            .Select(x => x.FileName)
            .Where(x => x != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var evidence in evidences.Where(x => !frozen.Contains(x)))
        {
            result.FileProblems.Add($"verification evidence not in the snapshot: {evidence}");
        }
    }

    /// <summary>
    /// Lists untracked (or ignored) files below a directory using git. Returns an empty set without git or outside a repository.
    /// </summary>
    private static HashSet<string> GitList(string directory, bool ignored)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var info = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            info.ArgumentList.Add("ls-files");
            info.ArgumentList.Add("-o");
            if (ignored)
            {
                info.ArgumentList.Add("-i");
            }
            info.ArgumentList.Add("--exclude-standard");
            info.ArgumentList.Add(".");
            using var process = Process.Start(info);
            if (process == null)
            {
                return set;
            }
            var text = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return set;
            }
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                set.Add(Path.GetFullPath(Path.Combine(directory, line.Trim())));
            }
        }
        catch
        {
            // git is optional: without it the git state is not checked
        }
        return set;
    }

    // value comparison -------------------------------------------------------

    /// <summary>
    /// Content to compare: inline content, or the text of a JSON content file (null without comparable content)
    /// </summary>
    private static string CompareContent(Entry entry, string lexDirectory)
    {
        if (!entry.HasContentFile)
        {
            return entry.Content;
        }
        try
        {
            return EntryMapping.Content(entry, lexDirectory, out _);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void Compare(CheckResult result, string entryKey, string content, RegulationValue value, string remainder)
    {
        JsonElement? target = null;
        string scalar = null;
        if (remainder.Length == 0)
        {
            target = value.Object;
            scalar = value.Scalar;
        }
        else
        {
            if (value.Object == null)
            {
                result.UnresolvedSplit++;
                AddSample(result.OrphanSamples, $"{entryKey}: path '{remainder}' but the regulation value is a scalar");
                return;
            }
            var current = value.Object.Value;
            foreach (var part in remainder.Split('.'))
            {
                JsonElement? next = null;
                if (current.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in current.EnumerateObject())
                    {
                        if (string.Equals(property.Name, part, StringComparison.OrdinalIgnoreCase))
                        {
                            next = property.Value;
                            break;
                        }
                    }
                }
                if (next == null)
                {
                    result.UnresolvedSplit++;
                    AddSample(result.OrphanSamples, $"{entryKey}: property '{part}' not found in the regulation value");
                    return;
                }
                current = next.Value;
            }
            if (current.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                target = current;
            }
            else
            {
                scalar = current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
            }
        }

        if (target == null && scalar == null)
        {
            return;
        }
        result.Compared++;
        var difference = Difference(target, scalar, content);
        if (difference != null)
        {
            result.Drift++;
            AddSample(result.DriftSamples, entryKey + ": " + difference);
        }
    }

    /// <summary>
    /// Difference between a regulation value (structured or scalar) and the lex content, null if equal
    /// </summary>
    internal static string Difference(JsonElement? structured, string scalar, string content)
    {
        if (structured != null)
        {
            try
            {
                using var lexJson = JsonDocument.Parse(content, JsonOptions);
                var differences = new List<string>();
                DeepDifference(structured.Value, lexJson.RootElement, string.Empty, differences);
                return DifferenceSummary(differences);
            }
            catch (JsonException)
            {
                return "lex content is not JSON but the regulation value is structured";
            }
        }
        return ScalarEquals(scalar, content)
            ? null
            : $"regulation '{Shorten(scalar)}' vs lex '{Shorten(content)}'";
    }

    /// <summary>Number of differences named in the summary of a structured value</summary>
    private const int MaxNamedDifferences = 3;

    /// <summary>
    /// Single difference as is, several as count with the first ones, null without difference
    /// </summary>
    private static string DifferenceSummary(List<string> differences)
    {
        if (differences.Count == 0)
        {
            return null;
        }
        if (differences.Count == 1)
        {
            return differences[0];
        }
        var named = string.Join("; ", differences.Take(MaxNamedDifferences));
        var more = differences.Count > MaxNamedDifferences ? $" (+{differences.Count - MaxNamedDifferences} more)" : string.Empty;
        return $"{differences.Count} differences: {named}{more}";
    }

    private static void DeepDifference(JsonElement regulation, JsonElement lex, string path, List<string> differences)
    {
        if (regulation.ValueKind == JsonValueKind.Object && lex.ValueKind == JsonValueKind.Object)
        {
            var lexProperties = lex.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var property in regulation.EnumerateObject())
            {
                if (!lexProperties.TryGetValue(property.Name, out var lexValue))
                {
                    differences.Add($"{path}/{property.Name} missing in lex");
                    continue;
                }
                DeepDifference(property.Value, lexValue, path + "/" + property.Name, differences);
            }
            var names = regulation.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in lexProperties.Keys.Where(n => !names.Contains(n)))
            {
                differences.Add($"{path}/{name} only in lex");
            }
            return;
        }
        if (regulation.ValueKind == JsonValueKind.Array && lex.ValueKind == JsonValueKind.Array)
        {
            var left = regulation.EnumerateArray().ToList();
            var right = lex.EnumerateArray().ToList();
            if (left.Count != right.Count)
            {
                differences.Add($"{path} array length {left.Count} vs {right.Count}");
                return;
            }
            for (var i = 0; i < left.Count; i++)
            {
                DeepDifference(left[i], right[i], $"{path}[{i}]", differences);
            }
            return;
        }
        if (regulation.ValueKind is JsonValueKind.Object or JsonValueKind.Array ||
            lex.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            differences.Add($"{path} structure differs");
            return;
        }
        var a = regulation.ValueKind == JsonValueKind.String ? regulation.GetString() : regulation.ToString();
        var b = lex.ValueKind == JsonValueKind.String ? lex.GetString() : lex.ToString();
        if (!ScalarEquals(a, b))
        {
            differences.Add($"{path} regulation '{Shorten(a)}' vs lex '{Shorten(b)}'");
        }
    }

    /// <summary>
    /// Numbers are compared numerically, text ignoring case
    /// </summary>
    internal static bool ScalarEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }
        a = a.Trim();
        b = b.Trim();
        if (decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var left) &&
            decimal.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var right))
        {
            return Math.Abs(left - right) < 0.0000001m;
        }
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // helpers ----------------------------------------------------------------

    private static void AddSample(List<string> samples, string text)
    {
        if (samples.Count < MaxSamples)
        {
            samples.Add(text);
        }
    }

    internal static string Shorten(string text) =>
        text is { Length: > 60 } ? text[..60] + "..." : text;
}
