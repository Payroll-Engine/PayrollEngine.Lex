using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
// ReSharper disable CommentTypo

namespace PayrollEngine.Lex;

/// <summary>
/// Compliance value entry in lex.json.
/// Exactly one of <see cref="Content"/> or <see cref="ContentFile"/> must be set.
/// </summary>
public sealed class Entry
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
    /// Reference to a local evidence document in the files[] array (e.g. "tax-rates-2026.pdf").
    /// The referenced file must be declared in the manifest's files[] array.
    /// </summary>
    public string DocumentRef { get; init; }

    /// <summary>
    /// Unit of measure (EUR, %, CHF, months, ...)
    /// </summary>
    public string Unit { get; init; }

    /// <summary>
    /// URLs where this specific value can be found; a value combined from several authorities
    /// lists one URL per authority, the first one is the main source
    /// </summary>
    public List<string> SourceUrls { get; init; }

    /// <summary>
    /// Precise location within the source document (free text)
    /// </summary>
    public string SourceLocation { get; init; }

    /// <summary>
    /// Verbatim quote from the source that holds the value; LexVerifySource looks up the numbers
    /// in the section of the quote
    /// </summary>
    public string SourceQuote { get; init; }

    /// <summary>
    /// Year the value belongs to legally when it differs from the cycle of the manifest (e.g. a surcharge
    /// for 2025 in the 2026 satellite, due with the annual return in January 2026); replaces the validFrom year in the year
    /// context of LexVerifySource and for the year of the source quote
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReferenceYear { get; init; }

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
    public Verification Verification { get; init; }

    /// <summary>
    /// Open entry: the value is taken from the previous cycle (LexRollover) and needs a new verification.
    /// A new verification record removes the flag.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Open { get; init; }

    /// <summary>
    /// Canonical text of the open flag, part of the snapshot source hash
    /// </summary>
    internal const string OpenHashInput = "open";

    /// <summary>
    /// Canonical text of the source quote, part of the snapshot source hash (null without quote)
    /// </summary>
    internal string QuoteHashInput() =>
        SourceQuote != null ? "sourceQuote:" + SourceQuote : null;

    /// <summary>
    /// Canonical text of the reference year, part of the snapshot source hash (null without reference year)
    /// </summary>
    internal string ReferenceYearHashInput() =>
        ReferenceYear != null ? "referenceYear:" + ReferenceYear.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

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

    /// <summary>
    /// The non-empty source URLs, in declaration order
    /// </summary>
    [JsonIgnore]
    public IEnumerable<string> Urls => SourceUrls?.Where(x => !string.IsNullOrWhiteSpace(x)) ?? [];
}
