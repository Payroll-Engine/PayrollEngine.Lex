using System.Collections.Generic;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Result of an attestation of manifest entries (<see cref="AttestOperation"/>)
/// </summary>
public sealed class AttestationResult
{
    /// <summary>Full path of lex.json, null when no manifest was found</summary>
    public string LexPath { get; internal init; }

    /// <summary>Error that prevented the attestation, nothing written</summary>
    public string Error { get; internal init; }

    /// <summary>Result per matched entry, in document order</summary>
    public List<AttestEntryResult> Entries { get; internal init; } = [];

    /// <summary>Records written</summary>
    public int Written => Entries.Count(x => x.Result == AttestResult.Written);

    /// <summary>Records equal to the existing record</summary>
    public int Unchanged => Entries.Count(x => x.Result == AttestResult.Unchanged);

    /// <summary>Entries skipped (existing record or review, no manual record to review)</summary>
    public int Skipped => Entries.Count - Written - Unchanged;
}
