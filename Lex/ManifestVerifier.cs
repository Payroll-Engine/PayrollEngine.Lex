using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Verification method of a text table verified by file identity, e.g. LexVerifySource 1.3.0; file identity
    /// </summary>
    public static string FileIdentityMethod { get; } = $"{SourceVerifier.Method}; file identity";

    /// <summary>
    /// Verify the entries of a manifest with source URLs (inline content and JSON content files) against the local
    /// evidence files or, with a live loader, against the source URLs. Per source URL the local evidence is the
    /// documentRef for the first URL, otherwise the declared file with the same URL.
    /// A text table (content map) is verified by file identity: its file (or archive) is declared in files[] with
    /// source URLs and present in the .lex folder; it is not downloaded, also not with a live loader.
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
            if (entry.HasContentMap && entry.HasContentFile)
            {
                result = VerifyFileIdentity(manifest, entry, lexDirectory, urls);
                evidence = result.Evidence;
            }
            else
            {
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
            }
            result.Evidence = evidence;
            result.ExcludedReason = exclusions.FirstOrDefault(x => x.Matches(entry.Key))?.Reason;
            results.Add(result);
        }
        return results;
    }

    /// <summary>
    /// Automatic verification record of a text table verified by file identity
    /// </summary>
    /// <param name="verifiedOn">Verification date</param>
    /// <param name="evidence">File or archive of the text table (declared in files[])</param>
    public static Verification FileIdentityVerification(DateOnly verifiedOn, string evidence) => new()
    {
        Mode = Verification.ModeAuto,
        VerifiedOn = verifiedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Method = FileIdentityMethod,
        Evidence = evidence
    };

    /// <summary>
    /// A text table is the authority file itself: its file (the archive of an archive entry) must be declared in
    /// files[] with source URLs and be present in the .lex folder. The numbers are not looked up.
    /// </summary>
    private static SourceResult VerifyFileIdentity(Manifest manifest, Entry entry, string lexDirectory, List<string> urls)
    {
        var source = ContentSource.Resolve(lexDirectory, entry.ContentFile);
        var problem = source.Problem();
        if (problem != null)
        {
            return new SourceResult { Key = entry.Key, Urls = urls, Match = SourceMatch.NoContent, Reason = problem };
        }
        var file = manifest.Files.FirstOrDefault(x => string.Equals(x.FileName, source.FileName, StringComparison.OrdinalIgnoreCase));
        if (file == null || !file.Urls.Any() || !File.Exists(Path.Combine(lexDirectory, source.FileName)))
        {
            var missing = $"text table {source.FileName} is not declared in files[] with sourceUrls and present in the .lex folder";
            return new SourceResult
            {
                Key = entry.Key, Urls = urls, Match = SourceMatch.NoEvidence, Reason = missing, Note = missing
            };
        }
        return new SourceResult
        {
            Key = entry.Key, Urls = urls, Match = SourceMatch.Found, FileIdentity = true, Evidence = source.FileName,
            Note = $"file identity: {source.FileName} from {string.Join(" ", file.Urls)}"
        };
    }

    private static string ContentFileText(Entry entry, string lexDirectory, out string reason)
    {
        try
        {
            return EntryMapping.Content(entry, lexDirectory, out reason);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
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
