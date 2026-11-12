using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Source verification of a manifest file with its write rules: the numbers of every entry are looked up in
/// the sources; on request the found source locations and the automatic verification records are written
/// </summary>
public static class VerifySourceOperation
{
    /// <summary>Environment variable of the host with the folder of additional language files</summary>
    public const string VocabularyVariable = "PAYROLL_ENGINE_LEX_VOCABULARY";

    /// <summary>
    /// Folder of additional language files: the given folder, otherwise the value of <see cref="VocabularyVariable"/>.
    /// The library reads no environment variable itself: the host passes its variable reader.
    /// </summary>
    /// <param name="vocabulary">Folder given by the caller, null or blank for none</param>
    /// <param name="getVariable">Reader of a host variable (e.g. <see cref="Environment.GetEnvironmentVariable(string)"/>),
    /// null without variable</param>
    public static string ResolveVocabulary(string vocabulary, Func<string, string> getVariable) =>
        string.IsNullOrWhiteSpace(vocabulary) ? getVariable?.Invoke(VocabularyVariable) : vocabulary;

    /// <summary>
    /// Verify the entries of a manifest file against their sources; the manifest is not changed
    /// </summary>
    /// <param name="lexPath">Path of lex.json</param>
    /// <param name="vocabulary">Folder of additional language files, null for the package languages only</param>
    /// <param name="loadLive">Loader of a live source URL (e.g. <see cref="SourceDocument.LoadLive"/>), null for the local evidence</param>
    /// <param name="documents">Loaded documents, shared over several manifests</param>
    /// <exception cref="JsonException">lex.json is not valid JSON</exception>
    /// <exception cref="ArgumentException">A language of the manifest without language file</exception>
    public static async Task<VerifySourceFileResult> VerifyFileAsync(string lexPath, string vocabulary = null,
        Func<string, Task<SourceDocument>> loadLive = null, IDictionary<string, SourceDocument> documents = null)
    {
        var json = await File.ReadAllTextAsync(lexPath);
        var manifest = JsonSerializer.Deserialize<Manifest>(json, LexJson.ManifestOptions) ?? new Manifest();
        var lexDirectory = Path.GetDirectoryName(lexPath)!;
        var results = await ManifestVerifier.VerifyManifest(manifest, lexDirectory, loadLive, documents, vocabulary);
        return new VerifySourceFileResult
        {
            LexPath = lexPath,
            Json = json,
            Manifest = manifest,
            Results = results,
            UnusedEvidence = EvidenceCheck.UnusedEvidence(manifest, lexDirectory)
        };
    }

    /// <summary>
    /// Write the results of a verification into its manifest file; the file is saved only when something changed.
    /// The result is also stored in <see cref="VerifySourceFileResult.Write"/>.
    /// </summary>
    /// <param name="file">Verification of the manifest file</param>
    /// <param name="mode">Write mode; <see cref="SourceWriteMode.ReportOnly"/> writes nothing and returns null</param>
    /// <param name="verifiedOn">Date of the verification records (default: today)</param>
    public static async Task<SourceWriteResult> WriteAsync(VerifySourceFileResult file, SourceWriteMode mode,
        DateOnly? verifiedOn = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (mode == SourceWriteMode.ReportOnly)
        {
            return null;
        }
        var editor = ManifestEditor.Parse(file.Json);
        var result = Write(editor, file.Results, mode == SourceWriteMode.RefreshLocations,
            verifiedOn ?? DateOnly.FromDateTime(DateTime.Today));
        if (editor.Changes > 0)
        {
            await editor.SaveAsync(file.LexPath);
            result.Saved = true;
        }
        file.Write = result;
        return result;
    }

    /// <summary>
    /// Write rules: the source location of found entries without one (with refresh also a location written by the
    /// source verification) and the automatic verification record of found entries. Ambiguous entries, entries
    /// with missing numbers and entries with an autoVerification exclusion are not written
    /// (<see cref="SourceResult.CanWrite"/>); a manual verification record is never replaced.
    /// A text table verified by file identity gets the file identity record.
    /// </summary>
    /// <param name="editor">Editor of the manifest</param>
    /// <param name="results">Verification results of the manifest entries</param>
    /// <param name="refresh">Replace source locations written by the source verification</param>
    /// <param name="verifiedOn">Date of the verification records</param>
    public static SourceWriteResult Write(ManifestEditor editor, IEnumerable<SourceResult> results, bool refresh,
        DateOnly verifiedOn)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(results);
        var result = new SourceWriteResult();
        foreach (var entry in results.Where(r => r.CanWrite))
        {
            if (entry.Location != null && editor.SetSourceLocation(entry.Key, entry.Location, refresh))
            {
                result.Locations++;
            }
            var verification = entry.FileIdentity
                ? ManifestVerifier.FileIdentityVerification(verifiedOn, entry.Evidence)
                : SourceVerifier.AutoVerification(verifiedOn, entry.Evidence, entry.Derived.Select(x => x.Property), entry.Languages);
            if (editor.SetAutoVerification(entry.Key, verification))
            {
                result.Verifications++;
            }
        }
        return result;
    }
}
