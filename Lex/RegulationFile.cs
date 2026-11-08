namespace PayrollEngine.Lex;

/// <summary>
/// Regulation JSON file with its content (without byte order mark)
/// </summary>
internal sealed class RegulationFile
{
    internal string Path { get; init; }
    internal byte[] Bom { get; init; }
    internal byte[] Content { get; init; }
}
