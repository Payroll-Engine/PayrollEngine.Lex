using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Verification of all entries of a lex manifest against their local or live sources
/// </summary>
public static class ManifestVerifier
{
    /// <summary>
    /// Verify the entries of a manifest with source URLs (inline content and JSON content files) against the local
    /// evidence files or, with a live loader, against the source URLs. Per source URL the local evidence is the
    /// documentRef for the first URL, otherwise the declared file with the same URL.
    /// Entries that reference an official calculator are skipped. The languages of the manifest
    /// (<see cref="Manifest.Languages"/>, default: all available languages) read the number and year words.
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <param name="loadLive">Loader of a live source URL (e.g. <see cref="SourceDocument.LoadLive"/>), null for the local evidence</param>
    /// <param name="documents">Loaded documents by URL (live) or local path, shared over several manifests</param>
    /// <param name="languageDirectory">Folder of additional language files (see <see cref="LanguageSet.Get(IEnumerable{string}, string)"/>),
    /// null for the package languages only</param>
    /// <exception cref="ArgumentException">A language of the manifest without language file</exception>
    public static async Task<List<SourceResult>> VerifyManifest(Manifest manifest, string lexDirectory,
        Func<string, Task<SourceDocument>> loadLive = null, IDictionary<string, SourceDocument> documents = null,
        string languageDirectory = null)
    {
        var languages = LanguageSet.Get(manifest.Languages, languageDirectory);
        documents ??= new Dictionary<string, SourceDocument>(StringComparer.Ordinal);
        var exclusions = manifest.Exclusions.Where(x => x.Kind == Exclusion.KindAutoVerification).ToList();
        var keys = DerivedProperties.Keys(manifest);
        var results = new List<SourceResult>();
        foreach (var entry in manifest.Values)
        {
            var urls = entry.Urls.ToList();
            if (urls.Count == 0 || string.IsNullOrEmpty(entry.Key) || Checker.IsReferenceEntry(entry.Key) ||
                (!entry.HasInlineContent && !entry.HasContentFile))
            {
                continue;
            }

            SourceResult result;
            string evidence = null;
            string reason = null;
            var content = entry.HasInlineContent ? entry.Content : ContentFileText(entry, lexDirectory, out reason);
            if (content == null)
            {
                result = new SourceResult
                {
                    Key = entry.Key, Urls = urls, Match = SourceMatch.NoContent, Reason = reason, Languages = languages.Label
                };
            }
            else
            {
                var sources = new List<(string Url, SourceDocument Document)>();
                for (var i = 0; i < urls.Count; i++)
                {
                    var url = urls[i];
                    SourceDocument document;
                    if (loadLive != null)
                    {
                        if (!documents.TryGetValue(url, out document))
                        {
                            documents[url] = document = await loadLive(url);
                        }
                    }
                    else
                    {
                        var localPath = LocalEvidence(manifest, entry, url, i == 0, lexDirectory);
                        if (localPath != null)
                        {
                            evidence ??= Path.GetFileName(localPath);
                        }
                        var cacheKey = localPath ?? string.Empty;
                        if (!documents.TryGetValue(cacheKey, out document))
                        {
                            documents[cacheKey] = document = await SourceDocument.LoadLocal(localPath);
                        }
                    }
                    sources.Add((url, document));
                }
                result = SourceVerifier.Verify(entry, sources, content, DerivedProperties.Of(entry.Key, manifest.Exclusions, keys),
                    manifest.ValidFrom, languages);
            }
            result.Evidence = evidence;
            result.ExcludedReason = exclusions.FirstOrDefault(x => x.Matches(entry.Key))?.Reason;
            results.Add(result);
        }
        return results;
    }

    private static string ContentFileText(Entry entry, string lexDirectory, out string reason)
    {
        try
        {
            return EntryMapping.Content(entry, lexDirectory, out reason);
        }
        catch (IOException exception)
        {
            reason = exception.Message;
            return null;
        }
    }

    /// <summary>
    /// Local evidence file of a source URL: the documentRef for the main source, otherwise a declared file
    /// with the same source URL
    /// </summary>
    private static string LocalEvidence(Manifest manifest, Entry entry, string url, bool mainSource, string lexDirectory)
    {
        var name = mainSource && !string.IsNullOrWhiteSpace(entry.DocumentRef)
            ? entry.DocumentRef
            : manifest.Files.FirstOrDefault(f => f.Urls.Contains(url, StringComparer.Ordinal))?.FileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        var path = Path.Combine(lexDirectory, name);
        return File.Exists(path) ? path : null;
    }
}
