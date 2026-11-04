using System;
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
public sealed class LexManifestEditor
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

    private LexManifestEditor(JsonNode root)
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
    public static LexManifestEditor Parse(string json) =>
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
    public bool SetAutoVerification(string key, LexVerification verification)
    {
        if (verification is not { Mode: LexVerification.ModeAuto })
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
            if (existing["mode"]?.GetValue<string>() != LexVerification.ModeAuto ||
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
        root["values"] is JsonArray values
            ? values.OfType<JsonObject>().FirstOrDefault(x => x["key"]?.GetValue<string>() == key)
            : null;
}
