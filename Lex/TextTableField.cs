namespace PayrollEngine.Lex;

/// <summary>
/// Column of a text table line mapped to a lookup key or value part
/// </summary>
public sealed class TextTableField
{
    /// <summary>
    /// Property name in the value object (required in the values map)
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Value type (default: text)
    /// </summary>
    public TextValueType ValueType { get; set; }

    /// <summary>
    /// 0-based character index of a fixed column, column index of a tab-delimited line
    /// </summary>
    public int Start { get; set; }

    /// <summary>
    /// Character count of a fixed column (required), ignored for tab-delimited lines
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Implied decimal places of a decimal value: the raw number is divided by 10^n (default: 0)
    /// </summary>
    public int DecimalPlaces { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({ValueType}) [{Start}-{Length}]";
}
