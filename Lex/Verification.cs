using System;
using System.Collections.Generic;

namespace PayrollEngine.Lex;

/// <summary>
/// Verification record of a lex value: how, when and by whom the value was checked against its source
/// </summary>
public sealed class Verification
{
    /// <summary>
    /// Automatic verification by a tool
    /// </summary>
    public const string ModeAuto = "auto";

    /// <summary>
    /// Manual verification by a person
    /// </summary>
    public const string ModeManual = "manual";

    /// <summary>
    /// Verification mode: auto or manual
    /// </summary>
    public string Mode { get; init; }

    /// <summary>
    /// Verification date in YYYY-MM-DD format
    /// </summary>
    public string VerifiedOn { get; init; }

    /// <summary>
    /// Person who verified the value (manual mode only)
    /// </summary>
    public string VerifiedBy { get; init; }

    /// <summary>
    /// Evidence document declared in the files[] array (mandatory in manual mode)
    /// </summary>
    public string Evidence { get; init; }

    /// <summary>
    /// Tool and version that verified the value (auto mode only, e.g. LexVerifySource 1.0)
    /// </summary>
    public string Method { get; init; }

    /// <summary>
    /// Second person who confirmed a manual verification (sources with four-eyes review)
    /// </summary>
    public string ReviewedBy { get; init; }

    /// <summary>
    /// Review date in YYYY-MM-DD format (sources with four-eyes review)
    /// </summary>
    public string ReviewedOn { get; init; }

    /// <summary>
    /// Properties of the entry that are derived from other values and not taken from the source
    /// (auto mode only, exclusion kind derived)
    /// </summary>
    public List<string> Derived { get; init; }

    /// <summary>
    /// Canonical text of the record, part of the snapshot source hash
    /// </summary>
    internal string HashInput() =>
        $"verification:{Mode}|{VerifiedOn}|{VerifiedBy}|{Evidence}|{Method}|{ReviewedBy}|{ReviewedOn}" +
        (Derived is { Count: > 0 } ? "|derived:" + string.Join(",", Derived) : string.Empty);

    /// <summary>
    /// Field-wise equality (ordinal)
    /// </summary>
    internal bool SameAs(Verification other) =>
        other != null && string.Equals(HashInput(), other.HashInput(), StringComparison.Ordinal);
}
