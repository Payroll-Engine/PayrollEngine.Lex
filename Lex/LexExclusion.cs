using System;

namespace PayrollEngine.Lex;

/// <summary>
/// Documented exception to the lex coverage rules. An excluded item is not a finding,
/// but it is listed by name in the check report.
/// </summary>
public sealed class LexExclusion
{
    /// <summary>Kind: regulation values without a lex entry</summary>
    public const string KindCoverage = "coverage";

    /// <summary>Kind: lex entries without sourceLocation</summary>
    public const string KindSourceLocation = "sourceLocation";

    /// <summary>Kind: lex entries without a regulation value (evidence of a rule that is implemented in code or derived values)</summary>
    public const string KindOrphan = "orphan";

    /// <summary>
    /// Exclusion kind: <c>coverage</c> (regulation values without lex entry),
    /// <c>sourceLocation</c> (lex entries without sourceLocation) or
    /// <c>orphan</c> (lex entries without regulation value)
    /// </summary>
    public string Kind { get; init; }

    /// <summary>
    /// Key pattern. Coverage: <c>Lookup</c>, <c>Lookup.Key</c> or a prefix with trailing <c>*</c>.
    /// SourceLocation: lex entry key, same syntax. A name also matches all keys below it (<c>Name.*</c>).
    /// </summary>
    public string Pattern { get; init; }

    /// <summary>
    /// Mandatory justification
    /// </summary>
    public string Reason { get; init; }

    /// <summary>
    /// Test if a key is matched by this exclusion
    /// </summary>
    internal bool Matches(string key)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(Pattern))
        {
            return false;
        }
        if (Pattern.EndsWith('*'))
        {
            return key.StartsWith(Pattern[..^1], StringComparison.Ordinal);
        }
        return string.Equals(key, Pattern, StringComparison.Ordinal) ||
               key.StartsWith(Pattern + ".", StringComparison.Ordinal);
    }
}
