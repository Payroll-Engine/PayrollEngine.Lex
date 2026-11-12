using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// JSON options shared by the lex checks
/// </summary>
internal static class LexJson
{
    /// <summary>Tolerant parsing of JSON content (comments, trailing commas)</summary>
    internal static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Reading of snapshot.json</summary>
    internal static readonly JsonSerializerOptions SnapshotOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Tolerant reading of lex.json (comments, trailing commas)</summary>
    internal static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Strict reading of lex.json for the snapshot: comments and trailing commas are errors</summary>
    internal static readonly JsonSerializerOptions StrictManifestOptions = new() { PropertyNameCaseInsensitive = true };
}
