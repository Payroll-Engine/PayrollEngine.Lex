using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup value of a regulation file with the location of its value token
/// </summary>
internal sealed class RegulationValue
{
    internal string Lookup { get; init; }
    internal string LookupAlias { get; set; }
    internal string Key { get; init; }
    internal string Scalar { get; init; }
    internal JsonElement? Object { get; init; }
    internal bool Covered { get; set; }
    internal RegulationLookup Owner { get; init; }

    /// <summary>Value token (value or valueObject), null without value</summary>
    internal JsonElement? Token { get; init; }
    internal int TokenOffset { get; init; } = -1;
    internal int TokenLength { get; init; }
}
