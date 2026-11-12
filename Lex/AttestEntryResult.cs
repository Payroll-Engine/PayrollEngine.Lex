namespace PayrollEngine.Lex;

/// <summary>
/// Attestation result of one entry
/// </summary>
public sealed class AttestEntryResult
{
    /// <summary>Entry key</summary>
    public string Key { get; internal init; }

    /// <summary>Result of the record</summary>
    public AttestResult Result { get; internal init; }

    /// <summary>A review was written although no source of the entry requires a review (<see cref="Source.ReviewFourEyes"/>)</summary>
    public bool ReviewNotRequired { get; internal init; }
}
