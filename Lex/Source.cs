using System;
using System.Text.Json.Serialization;

namespace PayrollEngine.Lex;

/// <summary>
/// Document-level source reference in lex.json
/// </summary>
public sealed class Source
{
    /// <summary>
    /// Review by the author only (default)
    /// </summary>
    public const string ReviewSingleAuthor = "SingleAuthor";

    /// <summary>
    /// Manual verifications require the confirmation of a second person
    /// </summary>
    public const string ReviewFourEyes = "FourEyes";

    /// <summary>
    /// URL to the source document or page
    /// </summary>
    public string Url { get; init; }

    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; init; }

    /// <summary>
    /// Legal basis (e.g. Income Tax Act §12)
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public string LegalBasis { get; init; }

    /// <summary>
    /// Review rule for the values of this source: SingleAuthor (default) or FourEyes.
    /// A value belongs to the source whose url equals the value's sourceUrl.
    /// </summary>
    public string Review { get; init; }

    /// <summary>
    /// True if manual verifications of this source require a second confirmation
    /// </summary>
    [JsonIgnore]
    public bool IsFourEyes => string.Equals(Review, ReviewFourEyes, StringComparison.Ordinal);
}
