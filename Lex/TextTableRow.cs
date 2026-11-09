using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup value converted from a text table line
/// </summary>
public sealed class TextTableRow
{
    /// <summary>1-based line number in the text table</summary>
    public int LineNumber { get; init; }

    /// <summary>Lookup value key, null without key mapping</summary>
    public string Key { get; init; }

    /// <summary>Composite key values (text, decimal, integer or boolean), null without keys mapping</summary>
    public List<object> KeyValues { get; init; }

    /// <summary>Range value, null without range value mapping</summary>
    public decimal? RangeValue { get; init; }

    /// <summary>Scalar value, null without value mapping</summary>
    public string Value { get; init; }

    /// <summary>Value object properties (text, decimal, integer or boolean), null without values mapping</summary>
    public Dictionary<string, object> ValueObject { get; init; }
}
