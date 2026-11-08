using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PayrollEngine.Lex;

/// <summary>
/// Writes the LexCheck results as Markdown report and CSV file
/// </summary>
public static class CheckReport
{
    /// <summary>
    /// Write the report (Markdown) and a CSV file with the same name
    /// </summary>
    /// <param name="results">Check results</param>
    /// <param name="reportPath">Path of the Markdown file; the CSV file uses the same name with the extension .csv</param>
    // ReSharper disable once UnusedMember.Global
    public static void Write(IReadOnlyList<CheckResult> results, string reportPath)
    {
        // the report folder is created on demand
        var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        // UTF-8 without byte order mark: the files are committed and compared in git
        File.WriteAllText(reportPath, BuildMarkdown(results), new UTF8Encoding(false));
        File.WriteAllText(Path.ChangeExtension(reportPath, ".csv"), BuildCsv(results), new UTF8Encoding(false));
    }

    /// <summary>
    /// CSV with one row per manifest; the column order is stable, new columns are appended at the end
    /// </summary>
    /// <param name="results">Check results</param>
    internal static string BuildCsv(IReadOnlyList<CheckResult> results)
    {
        var csv = new StringBuilder();
        // semicolon separator: opens in Excel with European locale settings
        csv.AppendLine("Satellite;LexPath;Snapshot;RegulationLookups;RegulationValues;LexEntries;CoveredDirect;CoveredByLookup;" +
                       "Uncovered;Orphans;UnresolvedSplit;Compared;Drift;MissingSourceUrls;MissingSourceLocation;" +
                       "MissingLegalReference;ValidationErrors;FileProblems;ValidFromMatch;Findings;ChangedSources;" +
                       "VerifiedAuto;VerifiedManual;Unverified;MissingReview;Open;UnspecificLocations;" +
                       "SecondaryMainSource;Release;Ambiguous;QuoteNotFound;ExcludedAutoVerified;ExcludedDerived;" +
                       "ExcludedNotApplicable;UnjustifiedZero;OtherReferenceYear;UnusedEvidence;NotVerifiable");
        // one row per satellite, values in the order of the header
        foreach (var r in results)
        {
            csv.AppendLine(string.Join(';',
                r.Satellite, r.LexPath, r.HasSnapshot, r.RegulationLookups, r.RegulationValues, r.LexEntries,
                r.CoveredDirect, r.CoveredByLookup, r.Uncovered, r.Orphans, r.UnresolvedSplit, r.Compared, r.Drift,
                r.MissingSourceUrls, r.MissingSourceLocation, r.MissingLegalReference, r.ValidationErrors.Count,
                r.FileProblems.Count, r.ValidFromMatch, r.HasFindings, r.ChangedSources.Count,
                r.VerifiedAuto, r.VerifiedManual, r.Unverified, r.MissingReview, r.Open, r.UnspecificLocations,
                r.SecondaryMainSource, r.Release, r.Ambiguous, r.QuoteNotFound, r.ExcludedAutoVerified, r.ExcludedDerived,
                r.ExcludedNotApplicable, r.UnjustifiedZero, r.OtherReferenceYear, r.UnusedEvidence, r.NotVerifiable));
        }
        return csv.ToString();
    }

    /// <summary>
    /// Markdown report: summary over all manifests, findings per manifest, verification table and documented exclusions
    /// </summary>
    /// <param name="results">Check results</param>
    internal static string BuildMarkdown(IReadOnlyList<CheckResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("# Lex check report");
        text.AppendLine();
        // the release gate adds measures that are no findings during development
        var release = results.Any(r => r.Release);
        text.AppendLine($"Created {DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} by `pecmd LexCheck`" +
                        (release ? " (release gate)." : "."));
        text.AppendLine();
        // summary: totals over all manifests (long: sums over many satellites)
        text.AppendLine("## Summary");
        text.AppendLine();
        text.AppendLine("| Measure | Value |");
        text.AppendLine("|:---|---:|");
        text.AppendLine($"| Manifests checked | {results.Count} |");
        text.AppendLine($"| Without findings | {results.Count(r => !r.HasFindings)} |");
        text.AppendLine($"| With findings | {results.Count(r => r.HasFindings)} |");
        text.AppendLine($"| Without snapshot | {results.Count(r => !r.HasSnapshot)} |");
        text.AppendLine($"| Regulation lookup values | {results.Sum(r => (long)r.RegulationValues)} |");
        text.AppendLine($"| Covered by an entry | {results.Sum(r => (long)r.CoveredDirect)} |");
        text.AppendLine($"| Covered at lookup level (e.g. table as file) | {results.Sum(r => (long)r.CoveredByLookup)} |");
        text.AppendLine($"| Without lex entry | {results.Sum(r => (long)r.Uncovered)} |");
        text.AppendLine($"| Lex entries without regulation value | {results.Sum(r => (long)r.Orphans)} |");
        text.AppendLine($"| Compared values | {results.Sum(r => (long)r.Compared)} |");
        text.AppendLine($"| Differences (candidates) | {results.Sum(r => (long)r.Drift)} |");
        text.AppendLine($"| Entries without sourceLocation | {results.Sum(r => (long)r.MissingSourceLocation)} |");
        text.AppendLine($"| Entries with a location that is not entry-specific | {results.Sum(r => (long)r.UnspecificLocations)} |");
        text.AppendLine($"| Entries with a secondary main source | {results.Sum(r => (long)r.SecondaryMainSource)} |");
        text.AppendLine($"| Entries verified automatically | {results.Sum(r => (long)r.VerifiedAuto)} |");
        text.AppendLine($"| Entries verified manually | {results.Sum(r => (long)r.VerifiedManual)} |");
        text.AppendLine($"| Entries without verification | {results.Sum(r => (long)r.Unverified)} |");
        text.AppendLine($"| Manual verifications without second review | {results.Sum(r => (long)r.MissingReview)} |");
        text.AppendLine($"| Open entries (previous cycle, verify again) | {results.Sum(r => (long)r.Open)} |");
        text.AppendLine($"| Entries with zero values without justification | {results.Sum(r => (long)r.UnjustifiedZero)} |");
        text.AppendLine($"| Entries with a reference year other than the validFrom year | {results.Sum(r => (long)r.OtherReferenceYear)} |");
        text.AppendLine($"| Evidence files that back no entry | {results.Sum(r => (long)r.UnusedEvidence)} |");
        if (release)
        {
            text.AppendLine($"| Ambiguous entries without manual verification | {results.Sum(r => (long)r.Ambiguous)} |");
            text.AppendLine($"| Entries with quote not found, without manual verification | {results.Sum(r => (long)r.QuoteNotFound)} |");
            text.AppendLine($"| Entries excluded from auto verification with an auto record | {results.Sum(r => (long)r.ExcludedAutoVerified)} |");
            text.AppendLine($"| Entries verifiable neither locally nor live | {results.Sum(r => (long)r.NotVerifiable)} |");
        }
        // URL measures only when the run checked the sources online (/checkurls)
        if (results.Any(r => r.UrlsChecked))
        {
            text.AppendLine($"| Source URLs not found | {results.Sum(r => (long)r.DeadUrls.Count)} |");
            text.AppendLine($"| Source URLs of blocking hosts (not verifiable) | {results.Sum(r => (long)r.BlockedUrls)} |");
            text.AppendLine($"| Sources compared with the snapshot | {results.Sum(r => (long)r.SourcesCompared)} |");
            text.AppendLine($"| Sources changed (check values) | {results.Sum(r => (long)r.ChangedSources.Count)} |");
            text.AppendLine($"| Sources not comparable (blocked or unreadable) | {results.Sum(r => (long)r.SourcesNotComparable)} |");
            text.AppendLine($"| Sources without text hash in the snapshot | {results.Sum(r => (long)r.SourcesWithoutBaseline)} |");
        }
        // documented exclusions are no findings, they are listed for transparency
        text.AppendLine($"| Excluded (documented): without lex entry | {results.Sum(r => (long)r.ExcludedCoverage)} |");
        text.AppendLine($"| Excluded (documented): without sourceLocation | {results.Sum(r => (long)r.ExcludedSourceLocation)} |");
        text.AppendLine($"| Excluded (documented): derived properties | {results.Sum(r => (long)r.ExcludedDerived)} |");
        text.AppendLine($"| Excluded (documented): properties not applicable | {results.Sum(r => (long)r.ExcludedNotApplicable)} |");
        text.AppendLine();
        text.AppendLine("A value difference is a candidate: numbers are compared numerically, text ignoring case. " +
                        "Check each candidate against the source. Freshness of the snapshot is checked by `LexVerify`.");

        text.AppendLine();
        // findings: one section per manifest with findings, samples limited to CheckResult.MaxSamples
        text.AppendLine("## Findings");
        foreach (var r in results.Where(x => x.HasFindings))
        {
            text.AppendLine();
            text.AppendLine($"### {r.Satellite}");
            text.AppendLine();
            text.AppendLine($"`{r.LexPath}`");
            text.AppendLine();
            if (r.Error != null)
            {
                text.AppendLine($"- {r.Error}");
            }
            foreach (var error in r.ValidationErrors)
            {
                text.AppendLine($"- Manifest: {error}");
            }
            if (!r.HasSnapshot)
            {
                text.AppendLine("- snapshot.json is missing");
            }
            text.AppendLine($"- Values: regulation {r.RegulationValues}, lex entries {r.LexEntries}; " +
                            $"covered {r.CoveredDirect}, at lookup level {r.CoveredByLookup}, without entry {r.Uncovered}; orphans {r.Orphans}");
            // coverage details: regulation values without entry and entries without regulation value
            if (r.Uncovered > 0)
            {
                text.AppendLine($"  - without entry: {Join(r.UncoveredSamples, r.Uncovered)}");
            }
            if (r.Orphans > 0 || r.UnresolvedSplit > 0)
            {
                text.AppendLine($"  - orphans / unresolved ({r.Orphans} + {r.UnresolvedSplit}): {Join(r.OrphanSamples, r.Orphans + r.UnresolvedSplit)}");
            }
            if (r.Drift > 0)
            {
                text.AppendLine($"- Differences ({r.Drift} of {r.Compared} compared): {Join(r.DriftSamples, r.Drift)}");
            }
            foreach (var url in r.DeadUrls)
            {
                text.AppendLine($"- Source URL not found: {url}");
            }
            foreach (var url in r.ChangedSources)
            {
                text.AppendLine($"- Source changed, check value: {url}");
            }
            // sourceLocation counts only when the run requires locations (/locationrequired)
            if (r.MissingSourceUrls + (r.RequireLocation ? r.MissingSourceLocation : 0) + r.MissingLegalReference > 0)
            {
                text.AppendLine($"- Missing fields: sourceUrls {r.MissingSourceUrls}, sourceLocation {r.MissingSourceLocation}, legalReference {r.MissingLegalReference}");
            }
            // verification state: without record (only when required), open and missing second review
            if (r.RequireVerification && r.Unverified > 0)
            {
                text.AppendLine($"- Without verification ({r.Unverified}): {Join(r.UnverifiedSamples, r.Unverified)}");
            }
            if (r.Open > 0)
            {
                text.AppendLine($"- Open entries, verify again ({r.Open}): {Join(r.OpenSamples, r.Open)}");
            }
            if (r.MissingReview > 0)
            {
                text.AppendLine($"- Manual verification without second review ({r.MissingReview}): {Join(r.MissingReviewSamples, r.MissingReview)}");
            }
            if (r.RequireLocation && r.UnspecificLocations > 0)
            {
                text.AppendLine($"- Location not entry-specific ({r.UnspecificLocations}): {Join(r.UnspecificLocationSamples, r.UnspecificLocations)}");
            }
            // release-only findings
            if (r.Release && r.SecondaryMainSource > 0)
            {
                text.AppendLine($"- Secondary main source ({r.SecondaryMainSource}): {Join(r.SecondaryMainSourceSamples, r.SecondaryMainSource)}");
            }
            if (r.Release && r.ManualVerificationRequired > 0)
            {
                text.AppendLine($"- Manual verification required ({r.ManualVerificationRequired}): {Join(r.ManualVerificationSamples, r.ManualVerificationRequired)}");
            }
            if (r.Release && r.UnjustifiedZero > 0)
            {
                text.AppendLine($"- Zero values without justification ({r.UnjustifiedZero}): {Join(r.UnjustifiedZeroSamples, r.UnjustifiedZero)}");
            }
            if (r.Release && r.UnusedEvidence > 0)
            {
                text.AppendLine($"- Evidence files that back no entry ({r.UnusedEvidence}): {Join(r.UnusedEvidenceSamples, r.UnusedEvidence)}");
            }
            if (r.Release && r.NotVerifiable > 0)
            {
                text.AppendLine($"- Verifiable neither locally nor live ({r.NotVerifiable}): {Join(r.NotVerifiableSamples, r.NotVerifiable)}");
            }
            if (r.FileProblems.Count > 0)
            {
                text.AppendLine($"- Evidence: {string.Join("; ", r.FileProblems)}");
            }
            // manifest and regulation must start on the same date
            if (!r.ValidFromMatch)
            {
                text.AppendLine($"- validFrom: lex {r.LexValidFrom}, regulation {r.RegulationValidFrom}");
            }
            if (r.Note != null)
            {
                text.AppendLine($"- Note: {r.Note}");
            }
        }
        text.AppendLine();
        // verification table: one row per manifest, also for manifests without findings
        text.AppendLine("## Verification");
        text.AppendLine();
        text.AppendLine("| Satellite | Entries | Auto | Manual | Without | Open | Without second review |");
        text.AppendLine("|:---|---:|---:|---:|---:|---:|---:|");
        // satellite names repeat across countries (Data.Tax.2026): such rows get the repository folder as prefix
        var duplicates = results.GroupBy(r => r.Satellite, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
        {
            text.AppendLine($"| {SatelliteLabel(r, duplicates)} | {r.LexEntries} | {r.VerifiedAuto} | {r.VerifiedManual} | {r.Unverified} | {r.Open} | {r.MissingReview} |");
        }

        // documented exclusions with their reasons, per manifest
        var excluded = results.Where(r => r.ExclusionNotes.Count > 0).ToList();
        if (excluded.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Documented exclusions");
            foreach (var r in excluded)
            {
                text.AppendLine();
                text.AppendLine($"### {r.Satellite} ({r.ExcludedCoverage} values, {r.ExcludedSourceLocation} entries without location, " +
                                $"{r.ExcludedOrphans} entries without value, {r.ExcludedAutoVerification} entries excluded from auto verification, " +
                                $"{r.ExcludedDerived} derived properties, {r.ExcludedNotApplicable} properties not applicable)");
                text.AppendLine();
                foreach (var note in r.ExclusionNotes)
                {
                    text.AppendLine($"- {note}");
                }
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// Satellite name, prefixed with the parent folder when several results share the name (e.g. a check over a root folder)
    /// </summary>
    private static string SatelliteLabel(CheckResult result, HashSet<string> duplicates)
    {
        if (!duplicates.Contains(result.Satellite ?? string.Empty) || string.IsNullOrEmpty(result.LexPath))
        {
            return result.Satellite;
        }
        // lex.json lives in <satellite>/.lex: two levels up is the satellite folder, its parent the repository folder
        var satelliteDirectory = Path.GetDirectoryName(Path.GetDirectoryName(result.LexPath));
        var parent = Path.GetFileName(Path.GetDirectoryName(satelliteDirectory));
        return string.IsNullOrEmpty(parent) ? result.Satellite : $"{parent}/{result.Satellite}";
    }

    /// <summary>
    /// Samples as code spans; "..." marks that the total exceeds the samples
    /// </summary>
    private static string Join(IReadOnlyCollection<string> samples, int total) =>
        string.Join("; ", samples.Select(s => $"`{s}`")) + (total > samples.Count ? ", ..." : string.Empty);
}
