namespace PayrollEngine.Lex;

/// <summary>
/// Regulation JSON file with its content (without byte order mark)
/// </summary>
internal sealed class RegulationFile
{
    /// <summary>Full path of the file</summary>
    internal string Path { get; init; }
    /// <summary>Byte order mark of the file, empty without</summary>
    internal byte[] Bom { get; init; }
    /// <summary>File content without byte order mark; token offsets refer to it</summary>
    internal byte[] Content { get; init; }
}
