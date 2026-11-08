using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup of a regulation file
/// </summary>
internal sealed class RegulationLookup
{
    internal string Name { get; init; }
    internal string Alias { get; set; }
    internal RegulationFile File { get; init; }

    /// <summary>Lookup has a values array</summary>
    internal bool HasValues { get; set; }

    /// <summary>Offset behind the last value object, or behind the opening bracket of an empty values array</summary>
    internal int InsertOffset { get; set; }

    /// <summary>Offset of the values property name (indentation of new values)</summary>
    internal int ValuesOffset { get; set; }

    /// <summary>Offset of the last value object</summary>
    internal int LastValueOffset { get; set; } = -1;

    internal List<RegulationValue> Values { get; } = [];
}
