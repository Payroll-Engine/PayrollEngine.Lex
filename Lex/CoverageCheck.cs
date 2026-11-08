using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Coverage of the regulation lookup values by lex entries, exclusions, zero values and reference years
/// </summary>
internal static class CoverageCheck
{
    internal static void CheckCoverage(Manifest manifest, string lexDirectory, CheckResult result, RegulationData regulation)
    {
        var coverageExclusions = manifest.Exclusions.Where(x => x.Kind == Exclusion.KindCoverage).ToList();
        var orphanExclusions = manifest.Exclusions.Where(x => x.Kind == Exclusion.KindOrphan).ToList();
        var usedExclusions = new HashSet<Exclusion>();

        var coveredLookups = MapEntries(manifest, lexDirectory, result, regulation, orphanExclusions, usedExclusions);
        CountCoverage(regulation.Values, result, coveredLookups, coverageExclusions, usedExclusions);

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
    /// Maps the lex entries to the regulation values: marks covered values, compares their contents and counts
    /// the entries without regulation value
    /// </summary>
    /// <returns>Lookups covered as a whole by an entry with content file</returns>
    private static HashSet<string> MapEntries(Manifest manifest, string lexDirectory, CheckResult result,
        RegulationData regulation, List<Exclusion> orphanExclusions, HashSet<Exclusion> usedExclusions)
    {
        var index = new RegulationIndex(regulation);
        var editionTag = EntryMapping.EditionTag(manifest);
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
                var content = ValueComparer.CompareContent(entry, lexDirectory);
                foreach (var value in target.Values)
                {
                    value.Covered = true;
                    if (content != null)
                    {
                        ValueComparer.Compare(result, entry.Key, content, value, target.Remainder);
                    }
                }
                continue;
            }
            // lookup-level coverage is reserved for entries backed by a content file (e.g. a whole table)
            if (target.Kind == EntryTargetKind.Lookup)
            {
                coveredLookups.Add(target.BaseKey);
                var content = ValueComparer.CompareContent(entry, lexDirectory);
                if (content != null && target.Lookup.Values.Count == 1)
                {
                    ValueComparer.Compare(result, entry.Key, content, target.Lookup.Values[0], string.Empty);
                }
                continue;
            }
            if (Checker.IsReferenceEntry(entry.Key))
            {
                // reference entry (link to an official calculator), not a regulation value
                continue;
            }
            CountOrphan(entry.Key, result, orphanExclusions, usedExclusions);
        }
        return coveredLookups;
    }

    private static void CountOrphan(string entryKey, CheckResult result, List<Exclusion> orphanExclusions,
        HashSet<Exclusion> usedExclusions)
    {
        var orphanExclusion = orphanExclusions.FirstOrDefault(x => x.Matches(entryKey));
        if (orphanExclusion != null)
        {
            usedExclusions.Add(orphanExclusion);
            result.ExcludedOrphans++;
            return;
        }
        result.Orphans++;
        CheckResult.AddSample(result.OrphanSamples, entryKey);
    }

    /// <summary>
    /// Counts the regulation values covered directly, by lookup, by exclusion or not at all
    /// </summary>
    private static void CountCoverage(IEnumerable<RegulationValue> values, CheckResult result, HashSet<string> coveredLookups,
        List<Exclusion> coverageExclusions, HashSet<Exclusion> usedExclusions)
    {
        foreach (var value in values)
        {
            if (value.Covered)
            {
                result.CoveredDirect++;
                continue;
            }
            if (coveredLookups.Contains(value.Lookup) ||
                (value.LookupAlias != null && coveredLookups.Contains(value.LookupAlias)))
            {
                result.CoveredByLookup++;
                continue;
            }
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
            CheckResult.AddSample(result.UncoveredSamples, fullKey);
        }
    }

    /// <summary>
    /// A derived or notApplicable exclusion must address an existing property of an entry (e.g. not renamed);
    /// a notApplicable property must have the value 0
    /// </summary>
    internal static void CheckPropertyExclusions(Manifest manifest, string lexDirectory, CheckResult result)
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
            if (!DerivedProperties.TryGetValue(ValueComparer.CompareContent(entry, lexDirectory), path, out var value))
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
                    $"Exclusion (notApplicable) '{exclusion.Pattern}': property '{path}' of entry '{key}' is {ValueComparer.Shorten(value)}, not 0: remove it");
                continue;
            }
            result.ExcludedNotApplicable++;
        }
    }

    /// <summary>
    /// Entries whose reference year differs from the validFrom year: listed as information for the review
    /// </summary>
    internal static void CheckReferenceYears(Manifest manifest, CheckResult result)
    {
        var validYear = manifest.ValidFrom is { Length: >= 4 } &&
                        int.TryParse(manifest.ValidFrom.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : (int?)null;
        foreach (var entry in manifest.Values.Where(x => x.ReferenceYear != null && x.ReferenceYear != validYear))
        {
            result.OtherReferenceYear++;
            CheckResult.AddSample(result.OtherReferenceYearSamples, $"{entry.Key} ({entry.ReferenceYear})");
        }
    }

    /// <summary>
    /// Zero properties of a bundle entry need a notApplicable exclusion (or a derived exclusion): a rate set to 0
    /// by mistake would otherwise never be looked up in the source
    /// </summary>
    internal static void CheckZeros(Manifest manifest, string lexDirectory, CheckResult result)
    {
        var keys = DerivedProperties.Keys(manifest);
        foreach (var entry in manifest.Values)
        {
            if (string.IsNullOrEmpty(entry.Key) || Checker.IsReferenceEntry(entry.Key))
            {
                continue;
            }
            var zeros = DerivedProperties.Zeros(ValueComparer.CompareContent(entry, lexDirectory));
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
            CheckResult.AddSample(result.UnjustifiedZeroSamples, $"{entry.Key} ({string.Join(", ", unjustified)})");
        }
    }
}
