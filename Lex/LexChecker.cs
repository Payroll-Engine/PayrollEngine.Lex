using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of a lex check of one data regulation satellite
/// </summary>
public sealed class LexCheckResult
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
    /// <summary>Lex entries without sourceUrl</summary>
    public int MissingSourceUrl { get; internal set; }

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

    // documented exclusions
    internal int ExcludedCoverage { get; set; }
    internal int ExcludedSourceLocation { get; set; }
    internal int ExcludedOrphans { get; set; }
    internal List<string> ExclusionNotes { get; } = [];

    /// <summary>Lex entries without legalReference</summary>
    public int MissingLegalReference { get; internal set; }

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
        UnresolvedSplit > 0 || Drift > 0 || MissingSourceUrl > 0 || (RequireLocation && MissingSourceLocation > 0) ||
        MissingLegalReference > 0 || FileProblems.Count > 0 || DeadUrls.Count > 0 || ChangedSources.Count > 0 ||
        !ValidFromMatch || Note != null;
}

/// <summary>
/// Checks a lex manifest against the lookup values of its data regulation satellite:
/// coverage, orphans, value differences, traceability fields, evidence files and validFrom.
/// </summary>
public static class LexChecker
{
    private const int MaxSamples = 8;

    /// <summary>Entries with this key prefix reference an official calculator and have no regulation value</summary>
    private const string ReferenceEntryPrefix = "OfficialCalculator.";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class RegulationValue
    {
        internal string Lookup { get; init; }
        internal string LookupAlias { get; set; }
        internal string Key { get; init; }
        internal string Scalar { get; init; }
        internal JsonElement? Object { get; init; }
        internal bool Covered { get; set; }
    }

    /// <summary>
    /// Check one satellite
    /// </summary>
    /// <param name="manifest">Manifest (lex.json)</param>
    /// <param name="lexPath">Full path of lex.json</param>
    /// <param name="requireLocation">Treat a missing sourceLocation as a finding</param>
    public static LexCheckResult Check(LexManifest manifest, string lexPath, bool requireLocation = false)
    {
        var lexDirectory = Path.GetDirectoryName(lexPath)!;
        var satelliteDirectory = Directory.GetParent(lexDirectory)?.FullName ?? lexDirectory;
        var result = new LexCheckResult
        {
            LexPath = lexPath,
            Satellite = Path.GetFileName(satelliteDirectory),
            RequireLocation = requireLocation,
            LexEntries = manifest.Values.Count,
            LexValidFrom = manifest.ValidFrom,
            HasSnapshot = File.Exists(Path.Combine(lexDirectory, "snapshot.json"))
        };

        // manifest structure (required fields, duplicates, content/contentFile, files)
        result.ValidationErrors.AddRange(LexValidator.Validate(manifest, lexDirectory));

        // regulation values
        var values = new List<RegulationValue>();
        var lookupNames = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            ReadRegulation(satelliteDirectory, result, values, lookupNames);
        }
        catch (Exception exception)
        {
            result.Error = "Regulation JSON could not be read: " + exception.Message;
            return result;
        }
        result.RegulationValues = values.Count;

        CheckTraceability(manifest, result);
        CheckValidFrom(result);
        CheckCoverage(manifest, result, values, lookupNames);
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

    private static void ReadRegulation(string satelliteDirectory, LexCheckResult result,
        List<RegulationValue> values, HashSet<string> lookupNames)
    {
        var regulationDirectory = Path.Combine(satelliteDirectory, "Regulation");
        if (!Directory.Exists(regulationDirectory))
        {
            return;
        }

        string satelliteNamespace = null;
        foreach (var file in Directory.GetFiles(regulationDirectory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(ReadBytes(file), JsonOptions);
            if (!document.RootElement.TryGetProperty("tenants", out var tenants) ||
                tenants.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var tenant in tenants.EnumerateArray())
            {
                if (!tenant.TryGetProperty("regulations", out var regulations) ||
                    regulations.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (var regulation in regulations.EnumerateArray())
                {
                    result.Regulation ??= GetString(regulation, "name");
                    result.RegulationValidFrom ??= GetString(regulation, "validFrom");
                    var ns = GetString(regulation, "namespace");
                    if (!string.IsNullOrEmpty(ns))
                    {
                        satelliteNamespace ??= ns;
                    }
                    if (!regulation.TryGetProperty("lookups", out var lookups) ||
                        lookups.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }
                    foreach (var lookup in lookups.EnumerateArray())
                    {
                        var name = GetString(lookup, "name");
                        if (name == null)
                        {
                            continue;
                        }
                        lookupNames.Add(name);
                        result.RegulationLookups++;
                        if (!lookup.TryGetProperty("values", out var lookupValues) ||
                            lookupValues.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }
                        foreach (var value in lookupValues.EnumerateArray())
                        {
                            var key = GetString(value, "key");
                            if (key == null && value.TryGetProperty("rangeValue", out var rangeValue))
                            {
                                key = rangeValue.ToString();
                            }
                            JsonElement? valueObject = null;
                            string scalar = null;
                            if (value.TryGetProperty("valueObject", out var vo) && vo.ValueKind != JsonValueKind.Null)
                            {
                                valueObject = vo.Clone();
                            }
                            else if (value.TryGetProperty("value", out var sv))
                            {
                                scalar = sv.ValueKind == JsonValueKind.String ? sv.GetString() : sv.ToString();
                            }
                            values.Add(new RegulationValue
                            {
                                Lookup = name,
                                Key = key ?? string.Empty,
                                Scalar = scalar,
                                Object = valueObject
                            });
                        }
                    }
                }
            }
        }

        // the lookup files of one satellite may omit the namespace that another file of the same satellite declares
        if (!string.IsNullOrEmpty(satelliteNamespace))
        {
            var prefix = satelliteNamespace + ".";
            foreach (var value in values)
            {
                if (value.Lookup.StartsWith(prefix, StringComparison.Ordinal))
                {
                    value.LookupAlias = value.Lookup[prefix.Length..];
                    lookupNames.Add(value.LookupAlias);
                }
            }
            foreach (var name in lookupNames.ToList())
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    lookupNames.Add(name[prefix.Length..]);
                }
            }
        }
    }

    private static void CheckTraceability(LexManifest manifest, LexCheckResult result)
    {
        var exclusions = manifest.Exclusions.Where(x => x.Kind == LexExclusion.KindSourceLocation).ToList();
        var used = new HashSet<LexExclusion>();
        foreach (var value in manifest.Values)
        {
            if (string.IsNullOrWhiteSpace(value.SourceUrl)) result.MissingSourceUrl++;
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

    private static void CheckValidFrom(LexCheckResult result)
    {
        if (result.LexValidFrom == null || result.RegulationValidFrom == null)
        {
            return;
        }
        result.ValidFromMatch = string.Equals(
            Left10(result.LexValidFrom), Left10(result.RegulationValidFrom), StringComparison.Ordinal);
    }

    private static string Left10(string value) => value.Length >= 10 ? value[..10] : value;

    private static void CheckCoverage(LexManifest manifest, LexCheckResult result,
        List<RegulationValue> values, HashSet<string> lookupNames)
    {
        var byFullKey = new Dictionary<string, List<RegulationValue>>(StringComparer.Ordinal);
        var byShortKey = new Dictionary<string, List<RegulationValue>>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            Add(byFullKey, value.Lookup + "." + value.Key, value);
            if (value.LookupAlias != null)
            {
                Add(byFullKey, value.LookupAlias + "." + value.Key, value);
            }
            Add(byShortKey, value.Key, value);
        }

        var coverageExclusions = manifest.Exclusions.Where(x => x.Kind == LexExclusion.KindCoverage).ToList();
        var orphanExclusions = manifest.Exclusions.Where(x => x.Kind == LexExclusion.KindOrphan).ToList();
        var usedExclusions = new HashSet<LexExclusion>();
        var coveredLookups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Values)
        {
            if (string.IsNullOrEmpty(entry.Key))
            {
                continue;
            }
            var matched = false;
            foreach (var (baseKey, remainder) in Splits(entry.Key))
            {
                if (byFullKey.TryGetValue(baseKey, out var list) || byShortKey.TryGetValue(baseKey, out list))
                {
                    matched = true;
                    foreach (var value in list)
                    {
                        value.Covered = true;
                        if (entry.HasInlineContent)
                        {
                            Compare(result, entry, value, remainder);
                        }
                    }
                    break;
                }
            }
            if (matched)
            {
                continue;
            }
            // lookup-level coverage is reserved for entries backed by a content file (e.g. a whole table)
            foreach (var (baseKey, _) in Splits(entry.Key))
            {
                if (entry.HasContentFile && lookupNames.Contains(baseKey))
                {
                    matched = true;
                    coveredLookups.Add(baseKey);
                    break;
                }
            }
            if (!matched && entry.Key.StartsWith(ReferenceEntryPrefix, StringComparison.Ordinal))
            {
                // reference entry (link to an official calculator), not a regulation value
                continue;
            }
            if (!matched)
            {
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

    private static void CheckEvidence(LexManifest manifest, string lexDirectory, LexCheckResult result)
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
                var path = LexHash.ResolvePath(lexDirectory, value.ContentFile);
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

    private static void Compare(LexCheckResult result, LexValue entry, RegulationValue value, string remainder)
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
                AddSample(result.OrphanSamples, $"{entry.Key}: path '{remainder}' but the regulation value is a scalar");
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
                    AddSample(result.OrphanSamples, $"{entry.Key}: property '{part}' not found in the regulation value");
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

        string difference;
        if (target != null)
        {
            result.Compared++;
            try
            {
                using var lexJson = JsonDocument.Parse(entry.Content, JsonOptions);
                difference = DeepDifference(target.Value, lexJson.RootElement, string.Empty);
            }
            catch (JsonException)
            {
                difference = "lex content is not JSON but the regulation value is structured";
            }
        }
        else if (scalar != null)
        {
            result.Compared++;
            difference = ScalarEquals(scalar, entry.Content)
                ? null
                : $"regulation '{Shorten(scalar)}' vs lex '{Shorten(entry.Content)}'";
        }
        else
        {
            return;
        }

        if (difference != null)
        {
            result.Drift++;
            AddSample(result.DriftSamples, entry.Key + ": " + difference);
        }
    }

    private static string DeepDifference(JsonElement regulation, JsonElement lex, string path)
    {
        if (regulation.ValueKind == JsonValueKind.Object && lex.ValueKind == JsonValueKind.Object)
        {
            var lexProperties = lex.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var property in regulation.EnumerateObject())
            {
                if (!lexProperties.TryGetValue(property.Name, out var lexValue))
                {
                    return $"{path}/{property.Name} missing in lex";
                }
                var difference = DeepDifference(property.Value, lexValue, path + "/" + property.Name);
                if (difference != null)
                {
                    return difference;
                }
            }
            var names = regulation.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in lexProperties.Keys.Where(n => !names.Contains(n)))
            {
                return $"{path}/{name} only in lex";
            }
            return null;
        }
        if (regulation.ValueKind == JsonValueKind.Array && lex.ValueKind == JsonValueKind.Array)
        {
            var left = regulation.EnumerateArray().ToList();
            var right = lex.EnumerateArray().ToList();
            if (left.Count != right.Count)
            {
                return $"{path} array length {left.Count} vs {right.Count}";
            }
            for (var i = 0; i < left.Count; i++)
            {
                var difference = DeepDifference(left[i], right[i], $"{path}[{i}]");
                if (difference != null)
                {
                    return difference;
                }
            }
            return null;
        }
        if (regulation.ValueKind is JsonValueKind.Object or JsonValueKind.Array ||
            lex.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return $"{path} structure differs";
        }
        var a = regulation.ValueKind == JsonValueKind.String ? regulation.GetString() : regulation.ToString();
        var b = lex.ValueKind == JsonValueKind.String ? lex.GetString() : lex.ToString();
        return ScalarEquals(a, b) ? null : $"{path} regulation '{Shorten(a)}' vs lex '{Shorten(b)}'";
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

    /// <summary>
    /// Candidate (lookup key, remaining property path) splits of a lex key, longest key first
    /// </summary>
    internal static IEnumerable<(string BaseKey, string Remainder)> Splits(string key)
    {
        yield return (key, string.Empty);
        var index = key.LastIndexOf('.');
        while (index > 0)
        {
            yield return (key[..index], key[(index + 1)..]);
            index = key.LastIndexOf('.', index - 1);
        }
    }

    private static void Add(Dictionary<string, List<RegulationValue>> map, string key, RegulationValue value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }
        list.Add(value);
    }

    private static void AddSample(List<string> samples, string text)
    {
        if (samples.Count < MaxSamples)
        {
            samples.Add(text);
        }
    }

    private static string Shorten(string text) =>
        text is { Length: > 60 } ? text[..60] + "..." : text;

    private static string GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => property.Value.ToString()
                };
            }
        }
        return null;
    }

    private static byte[] ReadBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes[3..] : bytes;
    }
}
