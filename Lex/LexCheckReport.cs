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
public static class LexCheckReport
{
    /// <summary>
    /// Write the report (Markdown) and a CSV file with the same name
    /// </summary>
    /// <param name="results">Check results</param>
    /// <param name="reportPath">Path of the Markdown file; the CSV file uses the same name with the extension .csv</param>
    public static void Write(IReadOnlyList<LexCheckResult> results, string reportPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(reportPath, BuildMarkdown(results), new UTF8Encoding(false));
        File.WriteAllText(Path.ChangeExtension(reportPath, ".csv"), BuildCsv(results), new UTF8Encoding(false));
    }

    internal static string BuildCsv(IReadOnlyList<LexCheckResult> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Satellite;LexPath;Snapshot;RegulationLookups;RegulationValues;LexEntries;CoveredDirect;CoveredByLookup;" +
                       "Uncovered;Orphans;UnresolvedSplit;Compared;Drift;MissingSourceUrl;MissingSourceLocation;" +
                       "MissingLegalReference;ValidationErrors;FileProblems;ValidFromMatch;Findings;ChangedSources");
        foreach (var r in results)
        {
            csv.AppendLine(string.Join(';',
                r.Satellite, r.LexPath, r.HasSnapshot, r.RegulationLookups, r.RegulationValues, r.LexEntries,
                r.CoveredDirect, r.CoveredByLookup, r.Uncovered, r.Orphans, r.UnresolvedSplit, r.Compared, r.Drift,
                r.MissingSourceUrl, r.MissingSourceLocation, r.MissingLegalReference, r.ValidationErrors.Count,
                r.FileProblems.Count, r.ValidFromMatch, r.HasFindings, r.ChangedSources.Count));
        }
        return csv.ToString();
    }

    internal static string BuildMarkdown(IReadOnlyList<LexCheckResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("# Lex check report");
        text.AppendLine();
        text.AppendLine($"Created {DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} by `pecmd LexCheck`.");
        text.AppendLine();
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
        if (results.Any(r => r.UrlsChecked))
        {
            text.AppendLine($"| Source URLs not found | {results.Sum(r => (long)r.DeadUrls.Count)} |");
            text.AppendLine($"| Source URLs of blocking hosts (not verifiable) | {results.Sum(r => (long)r.BlockedUrls)} |");
            text.AppendLine($"| Sources compared with the snapshot | {results.Sum(r => (long)r.SourcesCompared)} |");
            text.AppendLine($"| Sources changed (check values) | {results.Sum(r => (long)r.ChangedSources.Count)} |");
            text.AppendLine($"| Sources not comparable (blocked or unreadable) | {results.Sum(r => (long)r.SourcesNotComparable)} |");
            text.AppendLine($"| Sources without text hash in the snapshot | {results.Sum(r => (long)r.SourcesWithoutBaseline)} |");
        }
        text.AppendLine($"| Excluded (documented): without lex entry | {results.Sum(r => (long)r.ExcludedCoverage)} |");
        text.AppendLine($"| Excluded (documented): without sourceLocation | {results.Sum(r => (long)r.ExcludedSourceLocation)} |");
        text.AppendLine();
        text.AppendLine("A value difference is a candidate: numbers are compared numerically, text ignoring case. " +
                        "Check each candidate against the source. Freshness of the snapshot is checked by `LexVerify`.");

        text.AppendLine();
        text.AppendLine("## Findings");
        foreach (var r in results.Where(x => x.HasFindings))
        {
            text.AppendLine();
            text.AppendLine($"### {r.Satellite}");
            text.AppendLine();
            text.AppendLine($"`{r.LexPath}`");
            text.AppendLine();
            if (r.Error != null) text.AppendLine($"- {r.Error}");
            foreach (var error in r.ValidationErrors) text.AppendLine($"- Manifest: {error}");
            if (!r.HasSnapshot) text.AppendLine("- snapshot.json is missing");
            text.AppendLine($"- Values: regulation {r.RegulationValues}, lex entries {r.LexEntries}; " +
                            $"covered {r.CoveredDirect}, at lookup level {r.CoveredByLookup}, without entry {r.Uncovered}; orphans {r.Orphans}");
            if (r.Uncovered > 0) text.AppendLine($"  - without entry: {Join(r.UncoveredSamples, r.Uncovered)}");
            if (r.Orphans > 0 || r.UnresolvedSplit > 0) text.AppendLine($"  - orphans / unresolved ({r.Orphans} + {r.UnresolvedSplit}): {Join(r.OrphanSamples, r.Orphans + r.UnresolvedSplit)}");
            if (r.Drift > 0) text.AppendLine($"- Differences ({r.Drift} of {r.Compared} compared): {Join(r.DriftSamples, r.Drift)}");
            foreach (var url in r.DeadUrls) text.AppendLine($"- Source URL not found: {url}");
            foreach (var url in r.ChangedSources) text.AppendLine($"- Source changed, check value: {url}");
            if (r.MissingSourceUrl + (r.RequireLocation ? r.MissingSourceLocation : 0) + r.MissingLegalReference > 0)
            {
                text.AppendLine($"- Missing fields: sourceUrl {r.MissingSourceUrl}, sourceLocation {r.MissingSourceLocation}, legalReference {r.MissingLegalReference}");
            }
            if (r.FileProblems.Count > 0) text.AppendLine($"- Evidence: {string.Join("; ", r.FileProblems)}");
            if (!r.ValidFromMatch) text.AppendLine($"- validFrom: lex {r.LexValidFrom}, regulation {r.RegulationValidFrom}");
            if (r.Note != null) text.AppendLine($"- Note: {r.Note}");
        }
        var excluded = results.Where(r => r.ExclusionNotes.Count > 0).ToList();
        if (excluded.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Documented exclusions");
            foreach (var r in excluded)
            {
                text.AppendLine();
                text.AppendLine($"### {r.Satellite} ({r.ExcludedCoverage} values, {r.ExcludedSourceLocation} entries without location, {r.ExcludedOrphans} entries without value)");
                text.AppendLine();
                foreach (var note in r.ExclusionNotes) text.AppendLine($"- {note}");
            }
        }
        return text.ToString();
    }

    private static string Join(IReadOnlyCollection<string> samples, int total) =>
        string.Join("; ", samples.Select(s => $"`{s}`")) + (total > samples.Count ? ", ..." : string.Empty);
}
