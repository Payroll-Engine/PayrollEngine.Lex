using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup of a regulation file
/// </summary>
internal sealed class RegulationLookup
{
    /// <summary>Lookup name</summary>
    internal string Name { get; init; }
    /// <summary>Alternative lookup name, null without</summary>
    internal string Alias { get; set; }
    /// <summary>File of the lookup</summary>
    internal RegulationFile File { get; init; }

    /// <summary>Text of the rangeMode property, null without</summary>
    internal string RangeMode { get; init; }

    /// <summary>Range lookup: a rangeMode other than None; every value needs a rangeValue</summary>
    internal bool IsRange =>
        !string.IsNullOrWhiteSpace(RangeMode) &&
        !string.Equals(RangeMode, "None", System.StringComparison.OrdinalIgnoreCase) &&
        RangeMode != "0";

    /// <summary>Lookup has a values array</summary>
    internal bool HasValues { get; set; }

    /// <summary>Offset behind the last value object, or behind the opening bracket of an empty values array</summary>
    internal int InsertOffset { get; set; }

    /// <summary>Offset of the values property name (indentation of new values)</summary>
    internal int ValuesOffset { get; set; }

    /// <summary>Offset of the last value object</summary>
    internal int LastValueOffset { get; set; } = -1;

    /// <summary>Offset of the values array (opening bracket), -1 without values array</summary>
    internal int ArrayOffset { get; set; } = -1;

    /// <summary>Byte length of the values array including both brackets</summary>
    internal int ArrayLength { get; set; }

    /// <summary>Values of the lookup</summary>
    internal List<RegulationValue> Values { get; } = [];
}
