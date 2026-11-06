
namespace PayrollEngine.Lex;

/// <summary>
/// Documented exception to the lex coverage rules. An excluded item is not a finding,
/// but it is listed by name in the check report.
/// </summary>
public sealed class Exclusion
{
    /// <summary>Kind: regulation values without a lex entry</summary>
    public const string KindCoverage = "coverage";

    /// <summary>Kind: lex entries without sourceLocation</summary>
    public const string KindSourceLocation = "sourceLocation";

    /// <summary>Kind: lex entries without a regulation value (evidence of a rule that is implemented in code or derived values)</summary>
    public const string KindOrphan = "orphan";

    /// <summary>Kind: lex entries that are verified manually only; LexVerifySource writes no source location and
    /// no automatic verification record</summary>
    public const string KindAutoVerification = "autoVerification";

    /// <summary>Kind: property of a lex entry that is derived from other values (formula) and is not in the source;
    /// LexVerifySource does not look up its numbers</summary>
    public const string KindDerived = "derived";

    /// <summary>
    /// Exclusion kind: <c>coverage</c> (regulation values without lex entry),
    /// <c>sourceLocation</c> (lex entries without sourceLocation),
    /// <c>orphan</c> (lex entries without regulation value),
    /// <c>autoVerification</c> (lex entries verified manually only) or
    /// <c>derived</c> (derived property of a lex entry)
    /// </summary>
    public string Kind { get; init; }

    /// <summary>
    /// Key pattern. Coverage: <c>Lookup</c>, <c>Lookup.Key</c> or a prefix with trailing <c>*</c>.
    /// SourceLocation, orphan and autoVerification: lex entry key, same syntax. A name also matches all keys below it (<c>Name.*</c>).
    /// Derived: lex entry key and property path of the JSON content (<c>Lookup.Key.Property</c>, nested <c>Lookup.Key.Group.Property</c>).
    /// </summary>
    public string Pattern { get; init; }

    /// <summary>
    /// Mandatory justification; for a derived property the formula and the legal reference
    /// </summary>
    public string Reason { get; init; }

    /// <summary>
    /// Test if a key is matched by this exclusion
    /// </summary>
    internal bool Matches(string key) =>
        KeyPattern.Matches(Pattern, key);
}
