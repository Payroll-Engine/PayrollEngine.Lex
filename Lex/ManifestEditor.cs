using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Edits entries of a lex.json document in place: unknown properties and the property order are preserved
/// </summary>
public sealed class ManifestEditor
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions NodeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly JsonNode root;

    private ManifestEditor(JsonNode root)
    {
        this.root = root;
    }

    /// <summary>
    /// Number of changes since loading
    /// </summary>
    public int Changes { get; private set; }

    /// <summary>
    /// Parse a lex.json document
    /// </summary>
    /// <param name="json">lex.json text</param>
    public static ManifestEditor Parse(string json) =>
        new(JsonNode.Parse(json, documentOptions: DocumentOptions) ?? new JsonObject());

    /// <summary>
    /// Set the source location of an entry without one
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <param name="location">Source location</param>
    /// <param name="refresh">Also replace a location written by LexVerifySource (<see cref="SourceLocation.IsToolLocation"/>);
    /// a location written by a person is never replaced</param>
    /// <returns>True if the location was written</returns>
    public bool SetSourceLocation(string key, string location, bool refresh = false)
    {
        var entry = Entry(key);
        if (entry == null || string.IsNullOrWhiteSpace(location))
        {
            return false;
        }
        var existing = entry["sourceLocation"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(existing) &&
            (!refresh || !SourceLocation.IsToolLocation(existing) || string.Equals(existing, location, StringComparison.Ordinal)))
        {
            return false;
        }
        entry["sourceLocation"] = location;
        Changes++;
        return true;
    }

    /// <summary>
    /// Set the automatic verification record of an entry. A manual verification is never replaced;
    /// an automatic one only when method, evidence or derived properties change, so that a repeated run leaves
    /// the snapshot valid.
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <param name="verification">Automatic verification record</param>
    /// <returns>True if the record was written</returns>
    public bool SetAutoVerification(string key, Verification verification)
    {
        if (verification is not { Mode: Verification.ModeAuto })
        {
            throw new ArgumentException("Automatic verification record expected", nameof(verification));
        }
        var entry = Entry(key);
        if (entry == null)
        {
            return false;
        }
        if (entry["verification"] is JsonObject existing)
        {
            var derived = (existing["derived"] as JsonArray)?.Select(x => x?.GetValue<string>()).ToList() ?? [];
            if (existing["mode"]?.GetValue<string>() != Verification.ModeAuto ||
                (existing["method"]?.GetValue<string>() == verification.Method &&
                 existing["evidence"]?.GetValue<string>() == verification.Evidence &&
                 derived.SequenceEqual(verification.Derived ?? [], StringComparer.Ordinal)))
            {
                return false;
            }
        }
        entry["verification"] = JsonSerializer.SerializeToNode(verification, NodeOptions);
        entry.Remove("open");
        Changes++;
        return true;
    }

    /// <summary>
    /// Keys of the entries matched by a key or key pattern (<see cref="KeyPattern"/>), in document order
    /// </summary>
    /// <param name="pattern">Key, name or prefix with trailing *</param>
    public List<string> MatchKeys(string pattern) =>
        Values()
            .Select(x => x["key"]?.GetValue<string>())
            .Where(x => KeyPattern.Matches(pattern, x))
            .ToList();

    /// <summary>
    /// Test if an entry with the key exists
    /// </summary>
    /// <param name="key">Entry key</param>
    public bool HasKey(string key) => Entry(key) != null;

    /// <summary>
    /// Test if a file is declared in files[]
    /// </summary>
    /// <param name="fileName">File name</param>
    public bool HasFile(string fileName) =>
        Items("files").OfType<JsonObject>().Any(x =>
            string.Equals(x["fileName"]?.GetValue<string>(), fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Test if a source URL is declared in sources[]
    /// </summary>
    /// <param name="url">Source URL</param>
    public bool HasSource(string url) =>
        Items("sources").OfType<JsonObject>().Any(x =>
            string.Equals(x["url"]?.GetValue<string>(), url, StringComparison.Ordinal));

    /// <summary>
    /// Declare a source in sources[]. The properties follow the schema order (url, description, legalBasis, review,
    /// official); a missing sources array is created before values[].
    /// </summary>
    /// <param name="source">Source to declare: url is mandatory, review is SingleAuthor or FourEyes,
    /// official is written only when set (the schema default is true)</param>
    /// <returns>True if the source was added, false if a source with the URL is already declared (nothing changes)</returns>
    /// <exception cref="ArgumentException">Missing or relative URL, or an unknown review rule</exception>
    public bool AddSource(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var url = source.Url?.Trim();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Source URL expected (absolute URI)", nameof(source));
        }
        if (source.Review != null && source.Review != Source.ReviewSingleAuthor && source.Review != Source.ReviewFourEyes)
        {
            throw new ArgumentException(
                $"Review must be '{Source.ReviewSingleAuthor}' or '{Source.ReviewFourEyes}'", nameof(source));
        }
        if (HasSource(url))
        {
            return false;
        }
        var item = new JsonObject { ["url"] = url };
        if (!string.IsNullOrWhiteSpace(source.Description))
        {
            item["description"] = source.Description.Trim();
        }
        if (!string.IsNullOrWhiteSpace(source.LegalBasis))
        {
            item["legalBasis"] = source.LegalBasis.Trim();
        }
        if (source.Review != null)
        {
            item["review"] = source.Review;
        }
        if (source.Official != null)
        {
            item["official"] = source.Official.Value;
        }
        if (root["sources"] is not JsonArray sources)
        {
            sources = [];
            InsertRootProperty("sources", sources);
        }
        sources.Add(item);
        Changes++;
        return true;
    }

    /// <summary>
    /// Change the text properties of a declared source: description and legal basis. A property set to null stays,
    /// an empty text removes it; url, review rule and official flag are not changed here. A changed property keeps
    /// its position, a new property is inserted at its place in the source layout, unknown properties are kept.
    /// </summary>
    /// <param name="url">URL of the declared source (exact text)</param>
    /// <param name="update">Properties to change</param>
    /// <returns>Updated, Unchanged when all values already match, NotFound for an undeclared URL</returns>
    /// <exception cref="ArgumentException">The description of a secondary source (official false) would be removed</exception>
    public SourceUpdateResult UpdateSource(string url, SourceUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var source = Items("sources").OfType<JsonObject>().FirstOrDefault(x =>
            string.Equals(Text(x, "url"), url?.Trim(), StringComparison.Ordinal));
        if (source == null)
        {
            return SourceUpdateResult.NotFound;
        }
        var secondary = source["official"] is JsonValue official && official.TryGetValue<bool>(out var flag) && !flag;
        if (secondary && update.Description != null && string.IsNullOrWhiteSpace(update.Description))
        {
            throw new ArgumentException(
                "A secondary source (official false) needs a description that states why it is secondary", nameof(update));
        }
        var changed = SetText(source, "description", update.Description?.Trim(), removeEmpty: true, layout: SourceLayout);
        changed |= SetText(source, "legalBasis", update.LegalBasis?.Trim(), removeEmpty: true, layout: SourceLayout);
        if (!changed)
        {
            return SourceUpdateResult.Unchanged;
        }
        Changes++;
        return SourceUpdateResult.Updated;
    }

    /// <summary>
    /// Review rule of the sources of an entry (sources[].url listed in the sourceUrls of the entry):
    /// FourEyes when any source requires it
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <returns>The review rule, null without source or rule</returns>
    public string SourceReview(string key)
    {
        var urls = (Entry(key)?["sourceUrls"] as JsonArray)?
            .Select(x => x?.GetValue<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var reviews = Items("sources").OfType<JsonObject>()
            .Where(x => urls.Contains(x["url"]?.GetValue<string>() ?? string.Empty))
            .Select(x => x["review"]?.GetValue<string>())
            .Where(x => x != null)
            .ToList();
        return reviews.Contains(Source.ReviewFourEyes) ? Source.ReviewFourEyes : reviews.FirstOrDefault();
    }

    /// <summary>
    /// Append a new entry. The required fields content, sourceUrls, sourceLocation and legalReference
    /// are written empty when missing, so that LexCheck reports them until they are filled.
    /// </summary>
    /// <param name="value">Entry to add</param>
    /// <exception cref="InvalidOperationException">An entry with the key exists</exception>
    public void AddValue(Entry value)
    {
        if (string.IsNullOrWhiteSpace(value?.Key))
        {
            throw new ArgumentException("Entry key expected", nameof(value));
        }
        if (HasKey(value.Key))
        {
            throw new InvalidOperationException($"Entry {value.Key} exists");
        }
        var entry = new JsonObject
        {
            ["key"] = value.Key,
            ["content"] = value.Content ?? string.Empty
        };
        if (value.Unit != null)
        {
            entry["unit"] = value.Unit;
        }
        entry["sourceUrls"] = new JsonArray(value.Urls.Select(x => (JsonNode)x).ToArray());
        entry["sourceLocation"] = value.SourceLocation ?? string.Empty;
        if (value.SourceQuote != null)
        {
            entry["sourceQuote"] = value.SourceQuote;
        }
        if (value.ReferenceYear != null)
        {
            entry["referenceYear"] = value.ReferenceYear.Value;
        }
        entry["legalReference"] = value.LegalReference ?? string.Empty;
        if (value.DocumentRef != null)
        {
            entry["documentRef"] = value.DocumentRef;
        }
        if (value.Description != null)
        {
            entry["description"] = value.Description;
        }
        Items("values", create: true).Add(entry);
        Changes++;
    }

    /// <summary>
    /// Change fields of an existing entry. Properties set to null in <paramref name="update"/> stay as they are;
    /// a changed property keeps its position, a new property is inserted at its place in the entry layout,
    /// unknown properties and the verification record are not touched. A changed value does not renew
    /// the verification record: the caller decides about re-verification.
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <param name="update">Fields to change</param>
    /// <returns>Updated, Unchanged when all values already match, NotFound for an unknown key</returns>
    /// <exception cref="ArgumentException">A value is not valid (empty content, legal reference or source URLs,
    /// content that is not valid JSON, reference year out of range)</exception>
    /// <exception cref="InvalidOperationException">Content for an entry that uses a content file</exception>
    public EntryUpdateResult UpdateEntry(string key, EntryUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateUpdate(update);
        var entry = Entry(key);
        if (entry == null)
        {
            return EntryUpdateResult.NotFound;
        }
        if (update.Content != null && entry["contentFile"] != null)
        {
            throw new InvalidOperationException($"Entry {key} uses a content file: content cannot be set");
        }

        var changed = SetText(entry, "content", update.Content);
        if (update.SourceUrls != null)
        {
            var urls = update.SourceUrls.Select(x => x.Trim()).ToList();
            var existing = (entry["sourceUrls"] as JsonArray)?
                .Select(x => x is JsonValue v && v.TryGetValue<string>(out var text) ? text : null).ToList();
            if (existing == null || !existing.SequenceEqual(urls, StringComparer.Ordinal))
            {
                SetProperty(entry, "sourceUrls", new JsonArray(urls.Select(x => (JsonNode)x).ToArray()));
                changed = true;
            }
        }
        changed |= SetText(entry, "sourceQuote", update.SourceQuote, removeEmpty: true);
        if (update.RemoveReferenceYear)
        {
            changed |= entry.Remove("referenceYear");
        }
        else if (update.ReferenceYear != null)
        {
            var current = entry["referenceYear"] is JsonValue v && v.TryGetValue<int>(out var year) ? year : (int?)null;
            if (current != update.ReferenceYear)
            {
                SetProperty(entry, "referenceYear", update.ReferenceYear.Value);
                changed = true;
            }
        }
        changed |= SetText(entry, "legalReference", update.LegalReference);
        changed |= SetText(entry, "documentRef", update.DocumentRef?.Trim(), removeEmpty: true);
        changed |= SetText(entry, "description", update.Description, removeEmpty: true);

        if (!changed)
        {
            return EntryUpdateResult.Unchanged;
        }
        Changes++;
        return EntryUpdateResult.Updated;
    }

    /// <summary>
    /// Test if an exclusion with the kind and pattern exists
    /// </summary>
    /// <param name="kind">Exclusion kind</param>
    /// <param name="pattern">Exclusion pattern</param>
    public bool HasExclusion(string kind, string pattern) =>
        Items("exclusions").OfType<JsonObject>().Any(x =>
            string.Equals(Text(x, "kind"), kind, StringComparison.Ordinal) &&
            string.Equals(Text(x, "pattern"), pattern?.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// Append an exclusion. The kind must be known, pattern and reason are mandatory; a derived or notApplicable
    /// exclusion addresses a property of an existing entry (<c>Lookup.Key.Property</c>), never a whole entry.
    /// </summary>
    /// <param name="exclusion">Exclusion to add</param>
    /// <returns>True if the exclusion was added, false if an exclusion with the same kind and pattern exists</returns>
    /// <exception cref="ArgumentException">Unknown kind, missing pattern or reason, or an invalid property pattern</exception>
    public bool AddExclusion(Exclusion exclusion)
    {
        ArgumentNullException.ThrowIfNull(exclusion);
        if (!ExclusionKinds.Contains(exclusion.Kind))
        {
            throw new ArgumentException($"Unknown exclusion kind '{exclusion.Kind}'", nameof(exclusion));
        }
        var pattern = exclusion.Pattern?.Trim();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Exclusion pattern expected", nameof(exclusion));
        }
        if (string.IsNullOrWhiteSpace(exclusion.Reason))
        {
            throw new ArgumentException("Exclusion reason expected", nameof(exclusion));
        }
        if (DerivedProperties.IsPropertyKind(exclusion.Kind))
        {
            var keys = Values().Select(x => Text(x, "key")).Where(x => !string.IsNullOrEmpty(x)).ToHashSet(StringComparer.Ordinal);
            var (entryKey, path) = DerivedProperties.Resolve(pattern, keys);
            if (entryKey == null)
            {
                throw new ArgumentException(
                    $"Exclusion ({exclusion.Kind}) '{pattern}' addresses no entry: a property pattern starts with an entry key (Lookup.Key.Property)",
                    nameof(exclusion));
            }
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(
                    $"Exclusion ({exclusion.Kind}) '{pattern}' addresses the whole entry: a property is required (Lookup.Key.Property)",
                    nameof(exclusion));
            }
        }
        if (HasExclusion(exclusion.Kind, pattern))
        {
            return false;
        }
        Items("exclusions", create: true).Add(new JsonObject
        {
            ["kind"] = exclusion.Kind,
            ["pattern"] = pattern,
            ["reason"] = exclusion.Reason
        });
        Changes++;
        return true;
    }

    /// <summary>
    /// Declare a file in files[]
    /// </summary>
    /// <param name="file">File declaration</param>
    /// <returns>True if the file was added, false if a file with the name is declared</returns>
    public bool AddFile(EvidenceFile file)
    {
        if (string.IsNullOrWhiteSpace(file?.FileName))
        {
            throw new ArgumentException("File name expected", nameof(file));
        }
        if (HasFile(file.FileName))
        {
            return false;
        }
        Items("files", create: true).Add(JsonSerializer.SerializeToNode(file, NodeOptions));
        Changes++;
        return true;
    }

    /// <summary>
    /// Remove the verification record of an entry. Unknown keys and entries without a record are not an error.
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <returns>The removed record, or null when the entry is missing or had no verification record</returns>
    public RemovedVerification RemoveVerification(string key)
    {
        var entry = Entry(key);
        if (entry == null || entry["verification"] is not JsonObject verification)
        {
            return null;
        }
        var removed = new RemovedVerification
        {
            Mode = verification["mode"]?.GetValue<string>(),
            VerifiedBy = verification["verifiedBy"]?.GetValue<string>(),
            VerifiedOn = verification["verifiedOn"]?.GetValue<string>()
        };
        entry.Remove("verification");
        Changes++;
        return removed;
    }

    /// <summary>
    /// Set the manual verification record of an entry. An automatic record is replaced,
    /// a manual record only with overwrite.
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <param name="verification">Manual verification record</param>
    /// <param name="overwrite">Replace an existing manual record</param>
    public AttestResult SetManualVerification(string key, Verification verification, bool overwrite)
    {
        if (verification is not { Mode: Verification.ModeManual })
        {
            throw new ArgumentException("Manual verification record expected", nameof(verification));
        }
        var entry = Entry(key);
        if (entry == null)
        {
            return AttestResult.NotFound;
        }
        if (entry["verification"] is JsonObject existing && existing["mode"]?.GetValue<string>() == Verification.ModeManual)
        {
            if (existing["verifiedBy"]?.GetValue<string>() == verification.VerifiedBy &&
                existing["evidence"]?.GetValue<string>() == verification.Evidence &&
                existing["verifiedOn"]?.GetValue<string>() == verification.VerifiedOn)
            {
                return AttestResult.Unchanged;
            }
            if (!overwrite)
            {
                return AttestResult.ManualExists;
            }
        }
        entry["verification"] = JsonSerializer.SerializeToNode(verification, NodeOptions);
        entry.Remove("open");
        Changes++;
        return AttestResult.Written;
    }

    /// <summary>
    /// Set the second review (four-eyes) of a manual verification record
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <param name="reviewedBy">Reviewer</param>
    /// <param name="reviewedOn">Review date (yyyy-MM-dd)</param>
    /// <param name="overwrite">Replace an existing review</param>
    public AttestResult SetReview(string key, string reviewedBy, string reviewedOn, bool overwrite)
    {
        var entry = Entry(key);
        if (entry == null)
        {
            return AttestResult.NotFound;
        }
        if (entry["verification"] is not JsonObject verification ||
            verification["mode"]?.GetValue<string>() != Verification.ModeManual)
        {
            return AttestResult.NoManualRecord;
        }
        var existingBy = verification["reviewedBy"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(existingBy))
        {
            if (existingBy == reviewedBy && verification["reviewedOn"]?.GetValue<string>() == reviewedOn)
            {
                return AttestResult.Unchanged;
            }
            if (!overwrite)
            {
                return AttestResult.ReviewExists;
            }
        }
        verification["reviewedBy"] = reviewedBy;
        verification["reviewedOn"] = reviewedOn;
        Changes++;
        return AttestResult.Written;
    }

    /// <summary>
    /// Set the validFrom date of the manifest
    /// </summary>
    /// <param name="validFrom">Date in yyyy-MM-dd format</param>
    internal void SetValidFrom(string validFrom)
    {
        if (root is JsonObject header && header["validFrom"]?.GetValue<string>() != validFrom)
        {
            header["validFrom"] = validFrom;
            Changes++;
        }
    }

    /// <summary>
    /// Mark all entries as open: the verification record is removed
    /// </summary>
    /// <returns>Number of open entries</returns>
    internal int OpenEntries()
    {
        var count = 0;
        foreach (var entry in Values())
        {
            entry.Remove("verification");
            entry["open"] = true;
            count++;
        }
        Changes += count;
        return count;
    }

    /// <summary>
    /// Rename the content files of the entries and move the edition suffix of their keys to the new tag
    /// </summary>
    /// <param name="renameFile">Returns the new contentFile path, the same path when unchanged</param>
    /// <param name="oldTag">Previous edition tag (year)</param>
    /// <param name="newTag">New edition tag (year)</param>
    /// <returns>Number of renamed keys</returns>
    internal int RollContentFiles(Func<string, string> renameFile, string oldTag, string newTag)
    {
        var keys = 0;
        foreach (var entry in Values())
        {
            var contentFile = entry["contentFile"]?.GetValue<string>();
            if (string.IsNullOrEmpty(contentFile))
            {
                continue;
            }
            var renamed = renameFile(contentFile);
            if (!string.Equals(renamed, contentFile, StringComparison.Ordinal))
            {
                entry["contentFile"] = renamed;
                Changes++;
            }
            var key = entry["key"]?.GetValue<string>();
            if (key != null && key.EndsWith("." + oldTag, StringComparison.Ordinal))
            {
                entry["key"] = key[..^oldTag.Length] + newTag;
                Changes++;
                keys++;
            }
        }
        return keys;
    }

    /// <summary>
    /// The document as indented JSON
    /// </summary>
    public string ToJson() =>
        root.ToJsonString(WriteOptions) + "\n";

    /// <summary>
    /// Write the document (UTF-8 without BOM)
    /// </summary>
    /// <param name="path">Target path</param>
    // ReSharper disable once UnusedMember.Global
    public Task SaveAsync(string path) =>
        File.WriteAllTextAsync(path, ToJson(), new UTF8Encoding(false));

    private static readonly string[] ExclusionKinds =
    [
        Exclusion.KindCoverage, Exclusion.KindSourceLocation, Exclusion.KindOrphan,
        Exclusion.KindAutoVerification, Exclusion.KindDerived, Exclusion.KindNotApplicable
    ];

    /// <summary>Property order of an entry; a new property follows the nearest preceding property that is present</summary>
    private static readonly string[] EntryLayout =
    [
        "key", "content", "contentFile", "contentMap", "unit", "sourceUrls", "sourceLocation", "sourceQuote",
        "referenceYear", "legalReference", "documentRef", "description", "verification", "open"
    ];

    /// <summary>Property order of a declared source</summary>
    private static readonly string[] SourceLayout = ["url", "description", "legalBasis", "review", "official"];

    /// <summary>Property order of the manifest header and the arrays</summary>
    private static readonly string[] RootLayout =
    [
        "$schema", "authority", "regulation", "validFrom", "provider", "languages", "sources", "values", "files", "exclusions"
    ];

    /// <summary>Insert a new root property after the nearest preceding property of the root layout</summary>
    private void InsertRootProperty(string name, JsonNode value)
    {
        if (root is not JsonObject header)
        {
            throw new InvalidOperationException("The manifest is not a JSON object");
        }
        var index = 0;
        for (var i = Array.IndexOf(RootLayout, name) - 1; i >= 0; i--)
        {
            var before = header.IndexOf(RootLayout[i]);
            if (before >= 0)
            {
                index = before + 1;
                break;
            }
        }
        header.Insert(index, name, value);
    }

    private static void ValidateUpdate(EntryUpdate update)
    {
        if (update.Content != null)
        {
            if (string.IsNullOrWhiteSpace(update.Content))
            {
                throw new ArgumentException("Entry content must not be empty", nameof(update));
            }
            var error = EntryRules.ContentError(update.Content);
            if (error != null)
            {
                throw new ArgumentException(error, nameof(update));
            }
        }
        if (update.LegalReference != null && string.IsNullOrWhiteSpace(update.LegalReference))
        {
            throw new ArgumentException("Legal reference must not be empty", nameof(update));
        }
        if (update.SourceUrls != null &&
            (update.SourceUrls.Count == 0 || update.SourceUrls.Any(string.IsNullOrWhiteSpace)))
        {
            throw new ArgumentException("Source URLs must not be empty", nameof(update));
        }
        if (update.ReferenceYear is < 1990 or > 2100)
        {
            throw new ArgumentException($"Reference year {update.ReferenceYear} is not a year from 1990 to 2100", nameof(update));
        }
        if (update.RemoveReferenceYear && update.ReferenceYear != null)
        {
            throw new ArgumentException("Reference year cannot be set and removed", nameof(update));
        }
    }

    private static string Text(JsonObject item, string name) =>
        item[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// Set a text property: null leaves it, an empty text removes it when allowed, an equal text is no change
    /// </summary>
    private static bool SetText(JsonObject entry, string name, string value, bool removeEmpty = false,
        string[] layout = null)
    {
        if (value == null)
        {
            return false;
        }
        if (removeEmpty && value.Length == 0)
        {
            return entry.Remove(name);
        }
        if (string.Equals(Text(entry, name), value, StringComparison.Ordinal))
        {
            return false;
        }
        SetProperty(entry, name, value, layout);
        return true;
    }

    /// <summary>
    /// Replace a property in place, or insert a new one after the nearest preceding property of the entry layout
    /// </summary>
    private static void SetProperty(JsonObject entry, string name, JsonNode value, string[] layout = null)
    {
        if (entry.ContainsKey(name))
        {
            entry[name] = value;
            return;
        }
        layout ??= EntryLayout;
        var index = 0;
        for (var i = Array.IndexOf(layout, name) - 1; i >= 0; i--)
        {
            var before = entry.IndexOf(layout[i]);
            if (before >= 0)
            {
                index = before + 1;
                break;
            }
        }
        entry.Insert(index, name, value);
    }

    private JsonObject Entry(string key) =>
        Values().FirstOrDefault(x => x["key"]?.GetValue<string>() == key);

    private IEnumerable<JsonObject> Values() =>
        Items("values").OfType<JsonObject>();

    private JsonArray Items(string name, bool create = false)
    {
        if (root[name] is JsonArray array)
        {
            return array;
        }
        if (!create)
        {
            return [];
        }
        array = [];
        root[name] = array;
        return array;
    }
}
