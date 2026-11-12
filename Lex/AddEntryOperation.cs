using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Adds an entry to a manifest file. The entry must be new, its source URLs declared in sources[] and its
/// document in files[]; missing required fields are written empty and reported by the check.
/// </summary>
public static class AddEntryOperation
{
    /// <summary>
    /// Validate a new entry against the manifest: key and content format, existing key,
    /// source URL not declared in sources[], document not declared in files[]
    /// </summary>
    /// <param name="editor">Editor of the manifest</param>
    /// <param name="entry">New entry</param>
    /// <returns>Errors, empty when the entry can be added</returns>
    public static List<string> Validate(ManifestEditor editor, Entry entry)
    {
        var errors = new List<string>();
        var format = EntryRules.KeyError(entry?.Key) ?? EntryRules.ContentError(entry?.Content);
        if (format != null)
        {
            errors.Add(format);
            return errors;
        }
        if (editor.HasKey(entry!.Key))
        {
            errors.Add($"Entry {entry.Key} exists");
        }
        foreach (var url in entry.Urls.Where(x => !editor.HasSource(x)))
        {
            errors.Add($"Source URL is not declared in sources[]: {url}");
        }
        if (!string.IsNullOrWhiteSpace(entry.DocumentRef) && !editor.HasFile(entry.DocumentRef))
        {
            errors.Add($"Document is not declared in files[] (LexFetch): {entry.DocumentRef}");
        }
        return errors;
    }

    /// <summary>
    /// Required fields of the entry that are empty: content, sourceUrls, sourceLocation, legalReference
    /// </summary>
    /// <param name="entry">Entry</param>
    public static List<string> MissingFields(Entry entry)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(entry.Content)) missing.Add("content");
        if (!entry.Urls.Any()) missing.Add("sourceUrls");
        if (string.IsNullOrWhiteSpace(entry.SourceLocation)) missing.Add("sourceLocation");
        if (string.IsNullOrWhiteSpace(entry.LegalReference)) missing.Add("legalReference");
        return missing;
    }

    /// <summary>
    /// Add the entry to the manifest and save it
    /// </summary>
    /// <param name="lex">Path of lex.json or of its folder (default: lex.json or .lex/lex.json in the current folder)</param>
    /// <param name="entry">New entry</param>
    public static async Task<AddEntryResult> AddAsync(string lex, Entry entry)
    {
        var lexPath = FileResolver.ResolveManifest(lex);
        if (lexPath == null)
        {
            return new AddEntryResult { Errors = [$"lex.json not found: {lex ?? Directory.GetCurrentDirectory()}"] };
        }
        var editor = ManifestEditor.Parse(await File.ReadAllTextAsync(lexPath));
        var errors = Validate(editor, entry);
        if (errors.Count > 0)
        {
            return new AddEntryResult { LexPath = lexPath, Errors = errors };
        }
        editor.AddValue(entry);
        await editor.SaveAsync(lexPath);
        return new AddEntryResult { LexPath = lexPath, Missing = MissingFields(entry) };
    }
}
