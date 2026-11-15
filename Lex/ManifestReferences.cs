using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// References between the parts of a manifest and to the regulation: which entries an exclusion or a source
/// concerns, which exclusions concern an entry and whether an entry backs a regulation value. The removal of an
/// entry, a source or an exclusion is planned from these references.
/// </summary>
public static class ManifestReferences
{
    /// <summary>
    /// Keys of the entries an exclusion concerns. A property exclusion (derived, notApplicable) concerns the entry
    /// its pattern starts with; a coverage exclusion concerns regulation values, no entry.
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="exclusion">Exclusion of the manifest</param>
    /// <returns>Entry keys in document order</returns>
    public static List<string> EntriesOfExclusion(Manifest manifest, Exclusion exclusion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(exclusion);
        var keys = KeysOf(manifest);
        if (exclusion.Kind == Exclusion.KindCoverage)
        {
            return [];
        }
        if (DerivedProperties.IsPropertyKind(exclusion.Kind))
        {
            var (key, _) = DerivedProperties.Resolve(exclusion.Pattern, keys);
            return key == null ? [] : [key];
        }
        return keys.Where(x => KeyPattern.Matches(exclusion.Pattern, x)).ToList();
    }

    /// <summary>
    /// Exclusions that concern an entry
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="key">Entry key</param>
    /// <returns>The exclusions that concern the entry, and those among them that concern no other entry
    /// (they lose their purpose with the entry)</returns>
    public static (List<Exclusion> All, List<Exclusion> Exclusive) ExclusionsOfEntry(Manifest manifest, string key)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var all = new List<Exclusion>();
        var exclusive = new List<Exclusion>();
        foreach (var exclusion in manifest.Exclusions)
        {
            var entries = EntriesOfExclusion(manifest, exclusion);
            if (!entries.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }
            all.Add(exclusion);
            if (entries.Count == 1)
            {
                exclusive.Add(exclusion);
            }
        }
        return (all, exclusive);
    }

    /// <summary>
    /// Keys of the entries that list a source URL in their sourceUrls
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="url">Source URL (exact text)</param>
    /// <returns>Entry keys in document order</returns>
    public static List<string> EntriesOfSource(Manifest manifest, string url)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Values
            .Where(x => !string.IsNullOrEmpty(x.Key) && x.Urls.Any(u => string.Equals(u.Trim(), url?.Trim(), StringComparison.Ordinal)))
            .Select(x => x.Key)
            .ToList();
    }

    /// <summary>
    /// Names of the declared files that list a source URL in their sourceUrls
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="url">Source URL (exact text)</param>
    public static List<string> FilesOfSource(Manifest manifest, string url)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Files
            .Where(x => x.Urls.Any(u => string.Equals(u.Trim(), url?.Trim(), StringComparison.Ordinal)))
            .Select(x => x.FileName)
            .ToList();
    }

    /// <summary>
    /// Keys of the other entries that use the same evidence document (documentRef)
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="key">Entry key</param>
    /// <param name="documentRef">Evidence file of the entry</param>
    public static List<string> OtherEntriesOfDocument(Manifest manifest, string key, string documentRef)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(documentRef))
        {
            return [];
        }
        return manifest.Values
            .Where(x => !string.Equals(x.Key, key, StringComparison.Ordinal) &&
                        string.Equals(x.DocumentRef, documentRef, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Key)
            .ToList();
    }

    /// <summary>
    /// Regulation value or lookup an entry backs: the mapping of the check and the apply. An entry for a value that
    /// does not exist yet in an existing lookup (apply adds it), an entry without regulation target and a reference
    /// entry are not linked.
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="lexPath">Full path of lex.json (the regulation is read from the satellite folder)</param>
    /// <param name="key">Entry key</param>
    /// <returns>The link; not linked for an unknown key</returns>
    /// <exception cref="InvalidOperationException">The regulation JSON cannot be read</exception>
    public static RegulationLink RegulationLinkOf(Manifest manifest, string lexPath, string key)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(lexPath);
        var entry = manifest.Values.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal));
        if (entry == null)
        {
            return new RegulationLink();
        }
        var lexDirectory = Path.GetDirectoryName(Path.GetFullPath(lexPath))!;
        var satelliteDirectory = Directory.GetParent(lexDirectory)?.FullName ?? lexDirectory;
        RegulationData regulation;
        try
        {
            regulation = RegulationReader.Read(satelliteDirectory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("Regulation JSON could not be read: " + exception.Message, exception);
        }
        var target = EntryMapping.Resolve(entry, new RegulationIndex(regulation), EntryMapping.EditionTag(manifest));
        return target.Kind switch
        {
            EntryTargetKind.Values => new RegulationLink { Linked = true, Target = target.BaseKey, Values = target.Values.Count },
            EntryTargetKind.Lookup or EntryTargetKind.Table => new RegulationLink
            {
                Linked = true, Target = target.BaseKey, Values = target.Lookup?.Values.Count ?? 0
            },
            _ => new RegulationLink()
        };
    }

    private static List<string> KeysOf(Manifest manifest) =>
        manifest.Values.Select(x => x.Key).Where(x => !string.IsNullOrEmpty(x)).ToList();
}
