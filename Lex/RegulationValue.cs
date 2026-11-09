using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup value of a regulation file with the location of its value token
/// </summary>
internal sealed class RegulationValue
{
    /// <summary>Lookup name</summary>
    internal string Lookup { get; init; }
    /// <summary>Alternative lookup name, null without</summary>
    internal string LookupAlias { get; set; }
    /// <summary>Value key</summary>
    internal string Key { get; init; }
    /// <summary>Key property as written, null without key (range lookups)</summary>
    internal string ValueKey { get; init; }
    /// <summary>Key values array, null without</summary>
    internal JsonElement? KeyValues { get; init; }
    /// <summary>Range value as written (JSON text), null without</summary>
    internal string RangeValue { get; init; }
    /// <summary>Scalar value, null for a structured value</summary>
    internal string Scalar { get; init; }
    /// <summary>Structured value (object or array), null for a scalar</summary>
    internal JsonElement? Object { get; init; }
    /// <summary>Covered by a lex entry (set by the coverage check)</summary>
    internal bool Covered { get; set; }
    /// <summary>Lookup of the value</summary>
    internal RegulationLookup Owner { get; init; }

    /// <summary>Value token (value or valueObject), null without value</summary>
    internal JsonElement? Token { get; init; }
    /// <summary>Byte offset of the value token in the file, -1 without</summary>
    internal int TokenOffset { get; init; } = -1;
    /// <summary>Byte length of the value token</summary>
    internal int TokenLength { get; init; }
}
