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
    /// <returns>True if the location was written</returns>
    public bool SetSourceLocation(string key, string location)
    {
        var entry = Entry(key);
        if (entry == null || string.IsNullOrWhiteSpace(location) ||
            !string.IsNullOrWhiteSpace(entry["sourceLocation"]?.GetValue<string>()))
        {
            return false;
        }
        entry["sourceLocation"] = location;
        Changes++;
        return true;
    }

    /// <summary>
    /// Set the automatic verification record of an entry. A manual verification is never replaced;
    /// an automatic one only when method or evidence change, so that a repeated run leaves the snapshot valid.
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
            if (existing["mode"]?.GetValue<string>() != Verification.ModeAuto ||
                (existing["method"]?.GetValue<string>() == verification.Method &&
                 existing["evidence"]?.GetValue<string>() == verification.Evidence))
            {
                return false;
            }
        }
        entry["verification"] = JsonSerializer.SerializeToNode(verification, NodeOptions);
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
    /// Review rule of the source of an entry (sources[].url equals the sourceUrl of the entry)
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <returns>The review rule, null without source or rule</returns>
    public string SourceReview(string key)
    {
        var url = Entry(key)?["sourceUrl"]?.GetValue<string>();
        return url == null
            ? null
            : Items("sources").OfType<JsonObject>()
                .FirstOrDefault(x => x["url"]?.GetValue<string>() == url)?["review"]?.GetValue<string>();
    }

    /// <summary>
    /// Append a new entry. The required fields content, sourceUrl, sourceLocation and legalReference
    /// are written as empty text when missing, so that LexCheck reports them until they are filled.
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
        if (value.Unit != null) entry["unit"] = value.Unit;
        entry["sourceUrl"] = value.SourceUrl ?? string.Empty;
        entry["sourceLocation"] = value.SourceLocation ?? string.Empty;
        entry["legalReference"] = value.LegalReference ?? string.Empty;
        if (value.DocumentRef != null) entry["documentRef"] = value.DocumentRef;
        if (value.Description != null) entry["description"] = value.Description;
        Items("values", create: true).Add(entry);
        Changes++;
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
    /// The document as indented JSON
    /// </summary>
    public string ToJson() =>
        root.ToJsonString(WriteOptions) + "\n";

    /// <summary>
    /// Write the document (UTF-8 without BOM)
    /// </summary>
    /// <param name="path">Target path</param>
    public Task SaveAsync(string path) =>
        File.WriteAllTextAsync(path, ToJson(), new UTF8Encoding(false));

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
