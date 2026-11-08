namespace PayrollEngine.Lex;

/// <summary>
/// Result of fetching an evidence file
/// </summary>
public sealed class FetchResult
{
    /// <summary>File name in the .lex folder</summary>
    public string FileName { get; init; }

    /// <summary>Full path of the file</summary>
    public string FilePath { get; init; }

    /// <summary>SHA-256 of the downloaded content (lower-case hex)</summary>
    public string Sha256 { get; init; }

    /// <summary>Size of the downloaded content in bytes</summary>
    public long Size { get; init; }

    /// <summary>File state</summary>
    public FetchState State { get; init; }
}
