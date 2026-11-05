using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PayrollEngine.Lex;

/// <summary>
/// Source document to copy into the .lex folder
/// </summary>
public sealed class EvidenceFile
{
    /// <summary>
    /// Target file name in the .lex folder
    /// </summary>
    public string FileName { get; init; }

    /// <summary>
    /// Local path to the source file (copied by LexSnapshot)
    /// </summary>
    public string SourcePath { get; init; }

    /// <summary>
    /// Source URLs — when sourcePath is empty, the file is downloaded from the first URL that answers
    /// </summary>
    public List<string> SourceUrls { get; init; }

    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; init; }

    /// <summary>
    /// The non-empty source URLs, in declaration order
    /// </summary>
    [JsonIgnore]
    public IEnumerable<string> Urls => SourceUrls?.Where(x => !string.IsNullOrWhiteSpace(x)) ?? [];
}
