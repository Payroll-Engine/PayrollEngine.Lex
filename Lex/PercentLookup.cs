
// ReSharper disable CommentTypo

namespace PayrollEngine.Lex;

/// <summary>
/// Lookup of a value below 1 as percentage (0.085 as 8.5)
/// </summary>
internal enum PercentLookup
{
    /// <summary>No percentage: the value is looked up as written (factor, hours, currency amount)</summary>
    None,

    /// <summary>As percentage with a percent sign or word: 8.5%, 8,5 Prozent</summary>
    Sign,

    /// <summary>As percentage with or without a percent sign: a rate table of percentage points (2,60 for 0.026)</summary>
    Points
}
