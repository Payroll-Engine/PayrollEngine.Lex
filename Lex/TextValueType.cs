using System.Text.Json.Serialization;

namespace PayrollEngine.Lex;

/// <summary>
/// Type of value read from a text table column
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextValueType
{
    /// <summary>Text value (trimmed)</summary>
    Text,

    /// <summary>Decimal value, optionally with implied decimal places</summary>
    Decimal,

    /// <summary>Integer value</summary>
    Integer,

    /// <summary>Boolean value (true/false)</summary>
    Boolean
}
