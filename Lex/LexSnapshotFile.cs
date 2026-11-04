namespace PayrollEngine.Lex;

/// <summary>
/// File entry in snapshot.json — enriched with computed metadata
/// </summary>
public sealed class LexSnapshotFile
{
    /// <summary>
    /// Original file name
    /// </summary>
    public string FileName { get; init; }

    /// <summary>
    /// Relative path to the file in the .lex folder
    /// </summary>
    public string ContentFile { get; init; }

    /// <summary>
    /// MIME type (detected from extension)
    /// </summary>
    public string ContentType { get; init; }

    /// <summary>
    /// SHA256 hash of the file content
    /// </summary>
    public string Sha256 { get; init; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; init; }
}
