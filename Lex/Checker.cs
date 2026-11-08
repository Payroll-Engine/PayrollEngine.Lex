using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Checks a lex manifest against the lookup values of its data regulation satellite:
/// coverage, orphans, value differences, traceability fields, evidence files and validFrom.
/// </summary>
public static class Checker
{
    /// <summary>Entries with this key prefix reference an official calculator and have no regulation value</summary>
    private const string ReferenceEntryPrefix = "OfficialCalculator.";

    /// <summary>Entries with this key prefix reference an official calculator and have no regulation value</summary>
    internal static bool IsReferenceEntry(string key) =>
        key.StartsWith(ReferenceEntryPrefix, StringComparison.Ordinal);

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
        TraceabilityCheck.CheckVerification(manifest, result);

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

        TraceabilityCheck.CheckTraceability(manifest, result);
        TraceabilityCheck.CheckValidFrom(result);
        CoverageCheck.CheckCoverage(manifest, lexDirectory, result, regulation);
        CoverageCheck.CheckPropertyExclusions(manifest, lexDirectory, result);
        CoverageCheck.CheckZeros(manifest, lexDirectory, result);
        CoverageCheck.CheckReferenceYears(manifest, result);
        EvidenceCheck.CheckEvidence(manifest, lexDirectory, result);
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
    /// (<see cref="ManifestVerifier.VerifyManifest"/>); an ambiguous entry and an entry whose quote is not found
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
        var sources = await ManifestVerifier.VerifyManifest(manifest, Path.GetDirectoryName(result.LexPath)!);
        foreach (var source in sources.Where(x => !manual.Contains(x.Key)))
        {
            switch (source.Match)
            {
                case SourceMatch.NoEvidence or SourceMatch.Unreadable when !source.Urls.Any(readableLive.ContainsKey):
                    result.NotVerifiable++;
                    CheckResult.AddSample(result.NotVerifiableSamples, source.Match == SourceMatch.NoEvidence
                        ? $"{source.Key} (no local evidence)"
                        : $"{source.Key} (local evidence not readable: {source.Reason})");
                    break;
                case SourceMatch.Ambiguous:
                    result.Ambiguous++;
                    CheckResult.AddSample(result.ManualVerificationSamples, $"{source.Key} (ambiguous: {source.Ambiguity})");
                    break;
                case SourceMatch.QuoteNotFound:
                    result.QuoteNotFound++;
                    CheckResult.AddSample(result.ManualVerificationSamples, $"{source.Key} (quote not found)");
                    break;
            }
        }
        result.SourcesVerified = true;
    }
}
