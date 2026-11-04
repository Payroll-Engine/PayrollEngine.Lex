namespace PayrollEngine.Lex;

/// <summary>
/// Source document to copy into the .lex folder
/// </summary>
public sealed class LexFile
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
    /// Source URL — file is downloaded automatically when sourcePath is empty
    /// </summary>
    public string SourceUrl { get; init; }

    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; init; }
}
