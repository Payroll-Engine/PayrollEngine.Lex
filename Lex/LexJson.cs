using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// JSON options shared by the lex checks
/// </summary>
internal static class LexJson
{
    internal static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    internal static readonly JsonSerializerOptions SnapshotOptions = new() { PropertyNameCaseInsensitive = true };
}
