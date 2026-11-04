using System.Collections.Generic;
using System.Text.Json.Serialization;
// ReSharper disable CommentTypo

namespace PayrollEngine.Lex;

/// <summary>
/// Deserialized lex.json manifest — source of truth for compliance value provenance
/// </summary>
public sealed class LexManifest
{
    /// <summary>
    /// JSON Schema URI ($schema field)
    /// </summary>
    [JsonPropertyName("$schema")]
    // ReSharper disable once UnusedMember.Global
    public string Schema { get; init; }

    /// <summary>
    /// Country/jurisdiction code (e.g. DE, CH, SG)
    /// </summary>
    public string Authority { get; init; }

    /// <summary>
    /// Regulation identifier (e.g. Data.Tax, Data.Rates)
    /// </summary>
    public string Regulation { get; init; }

    /// <summary>
    /// Effective date in YYYY-MM-DD format
    /// </summary>
    public string ValidFrom { get; init; }

    /// <summary>
    /// Issuing authority (e.g. Tax authority)
    /// </summary>
    public string Provider { get; init; }

    /// <summary>
    /// Document-level source references
    /// </summary>
    public List<LexSource> Sources { get; init; } = [];

    /// <summary>
    /// Compliance values with content and provenance
    /// </summary>
    public List<LexValue> Values { get; init; } = [];

    /// <summary>
    /// Source documents to copy into the .lex folder
    /// </summary>
    public List<LexFile> Files { get; init; } = [];

    /// <summary>
    /// Documented exclusions from the coverage and sourceLocation checks
    /// </summary>
    public List<LexExclusion> Exclusions { get; init; } = [];
}
