using System;
using System.Collections.Generic;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Traceability fields of lex entries: sources, legal reference, location, verification and validFrom
/// </summary>
internal static class TraceabilityCheck
{
    /// <summary>Minimum number of entries sharing a location to treat it as not entry-specific</summary>
    private const int SharedLocationEntries = 5;
    /// <summary>Minimum length of a shared location; short shared locations (e.g. a table name) are fine</summary>
    private const int SharedLocationLength = 200;
    /// <summary>A longer source location is a pasted text, not a location</summary>
    internal const int MaxLocationLength = 500;

    /// <summary>
    /// Counts entries without source URL, legal reference or specific source location
    /// </summary>
    internal static void CheckTraceability(Manifest manifest, CheckResult result)
    {
        var exclusions = manifest.Exclusions.Where(x => x.Kind == Exclusion.KindSourceLocation).ToList();
        var used = new HashSet<Exclusion>();
        // URLs declared as secondary sources (e.g. commentaries) must not be the main source
        var secondary = manifest.Sources
            .Where(x => !x.IsOfficial && !string.IsNullOrWhiteSpace(x.Url))
            .Select(x => x.Url)
            .ToHashSet(StringComparer.Ordinal);
        var shared = SharedLocations(manifest);
        foreach (var value in manifest.Values)
        {
            CheckSources(value, result, secondary);
            CheckLocation(value, result, shared, exclusions, used);
            if (string.IsNullOrWhiteSpace(value.LegalReference))
            {
                result.MissingLegalReference++;
            }
        }
        // unused exclusions are errors: they hide future gaps
        foreach (var exclusion in exclusions.Where(x => !used.Contains(x)))
        {
            result.ValidationErrors.Add($"Exclusion (sourceLocation) '{exclusion.Pattern}' matches no entry without sourceLocation: remove it");
        }
    }

    /// <summary>
    /// Long source locations repeated by several entries (not entry-specific)
    /// </summary>
    private static HashSet<string> SharedLocations(Manifest manifest) =>
        manifest.Values
            .Select(x => x.SourceLocation?.Trim())
            .Where(x => x is { Length: > SharedLocationLength })
            .GroupBy(x => x, StringComparer.Ordinal)
            .Where(g => g.Count() >= SharedLocationEntries)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// An entry needs a source URL; its main source must not be a secondary source
    /// </summary>
    private static void CheckSources(Entry value, CheckResult result, HashSet<string> secondary)
    {
        if (!value.Urls.Any())
        {
            result.MissingSourceUrls++;
        }
        var mainSource = value.Urls.FirstOrDefault();
        if (mainSource != null && secondary.Contains(mainSource))
        {
            result.SecondaryMainSource++;
            CheckResult.AddSample(result.SecondaryMainSourceSamples, $"{value.Key}: {mainSource}");
        }
    }

    /// <summary>
    /// An entry needs an entry-specific source location, unless excluded
    /// </summary>
    private static void CheckLocation(Entry value, CheckResult result, HashSet<string> shared, List<Exclusion> exclusions,
        HashSet<Exclusion> used)
    {
        // a too long or shared location counts as unspecific, but not as missing
        var location = value.SourceLocation?.Trim();
        if (location != null && (location.Length > MaxLocationLength || shared.Contains(location)))
        {
            result.UnspecificLocations++;
            CheckResult.AddSample(result.UnspecificLocationSamples, value.Key);
        }
        if (!string.IsNullOrWhiteSpace(value.SourceLocation))
        {
            return;
        }
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

    /// <summary>
    /// Counts the verification records of the entries: open, auto, manual and unverified
    /// </summary>
    internal static void CheckVerification(Manifest manifest, CheckResult result)
    {
        // manual verifications of entries from these sources need a review
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
            // an open entry waits for a new verification, its old record is not counted
            if (value.Open)
            {
                result.Open++;
                CheckResult.AddSample(result.OpenSamples, value.Key);
                continue;
            }
            CountVerification(value, result, autoExclusion != null, fourEyesUrls);
        }
        foreach (var exclusion in autoExclusions.Where(x => !usedExclusions.Contains(x)))
        {
            result.ValidationErrors.Add($"Exclusion (autoVerification) '{exclusion.Pattern}' matches no entry: remove it");
        }
    }

    /// <summary>
    /// Counts the verification record of an entry: auto (not allowed when excluded), manual (four-eyes review
    /// when a source requires it) or without
    /// </summary>
    private static void CountVerification(Entry value, CheckResult result, bool autoExcluded, HashSet<string> fourEyesUrls)
    {
        var verification = value.Verification;
        switch (verification?.Mode)
        {
            case Verification.ModeAuto:
                result.VerifiedAuto++;
                if (autoExcluded)
                {
                    result.ExcludedAutoVerified++;
                    CheckResult.AddSample(result.ManualVerificationSamples, $"{value.Key} (excluded from auto verification)");
                }
                break;
            case Verification.ModeManual:
                result.VerifiedManual++;
                if (value.Urls.Any(fourEyesUrls.Contains) &&
                    (string.IsNullOrWhiteSpace(verification.ReviewedBy) || string.IsNullOrWhiteSpace(verification.ReviewedOn)))
                {
                    result.MissingReview++;
                    CheckResult.AddSample(result.MissingReviewSamples, value.Key);
                }
                break;
            default:
                result.Unverified++;
                CheckResult.AddSample(result.UnverifiedSamples, value.Key);
                break;
        }
    }

    /// <summary>
    /// Compares the date part of validFrom in lex.json and in the regulation
    /// </summary>
    internal static void CheckValidFrom(CheckResult result)
    {
        if (result.LexValidFrom == null || result.RegulationValidFrom == null)
        {
            return;
        }
        result.ValidFromMatch = string.Equals(
            Left10(result.LexValidFrom), Left10(result.RegulationValidFrom), StringComparison.Ordinal);
    }

    /// <summary>
    /// Date part (yyyy-MM-dd) of a date or date-time
    /// </summary>
    private static string Left10(string value) => value.Length >= 10 ? value[..10] : value;
}
