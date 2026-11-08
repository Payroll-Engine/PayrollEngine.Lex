namespace PayrollEngine.Lex;

/// <summary>
/// Location of a string token in a regulation file
/// </summary>
internal sealed class RegulationToken
{
    internal RegulationFile File { get; init; }
    internal string Value { get; init; }

    /// <summary>Offset of the opening quote</summary>
    internal int Offset { get; init; }

    /// <summary>Length including the quotes</summary>
    internal int Length { get; init; }
}
