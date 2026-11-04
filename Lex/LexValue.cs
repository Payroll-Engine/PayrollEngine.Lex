using System.Text.Json.Serialization;
// ReSharper disable CommentTypo

namespace PayrollEngine.Lex;

/// <summary>
/// Compliance value entry in lex.json.
/// Exactly one of <see cref="Content"/> or <see cref="ContentFile"/> must be set.
/// </summary>
public sealed class LexValue
{
    /// <summary>
    /// Unique value identifier within the manifest
    /// </summary>
    public string Key { get; init; }

    /// <summary>
    /// Inline value — scalar, JSON object, or JSON array (mutually exclusive with ContentFile)
    /// </summary>
    public string Content { get; init; }

    /// <summary>
    /// Relative path to a source file to copy into the .lex folder (mutually exclusive with Content)
    /// </summary>
    public string ContentFile { get; init; }

    /// <summary>
    /// Reference to a local evidence document in the files[] array (e.g. "ahv-beitraege-2026.pdf").
    /// The referenced file must be declared in the manifest's files[] array.
    /// </summary>
    public string DocumentRef { get; init; }

    /// <summary>
    /// Unit of measure (EUR, %, CHF, months, ...)
    /// </summary>
    public string Unit { get; init; }

    /// <summary>
    /// URL where this specific value can be found
    /// </summary>
    public string SourceUrl { get; init; }

    /// <summary>
    /// Precise location within the source document (free text)
    /// </summary>
    public string SourceLocation { get; init; }

    /// <summary>
    /// Legal norm reference (e.g. Income Tax Act §12(1))
    /// </summary>
    public string LegalReference { get; init; }

    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; init; }

    /// <summary>
    /// Verification record (optional; LexCheck reports values without verification)
    /// </summary>
    public LexVerification Verification { get; init; }

    /// <summary>
    /// Returns true if this value uses an inline content string
    /// </summary>
    [JsonIgnore]
    public bool HasInlineContent => Content != null;

    /// <summary>
    /// Returns true if this value references an external file
    /// </summary>
    [JsonIgnore]
    public bool HasContentFile => ContentFile != null;
}
