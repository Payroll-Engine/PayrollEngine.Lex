namespace PayrollEngine.Lex;

/// <summary>
/// Verification record removed from an entry (<see cref="ManifestEditor.RemoveVerification"/>)
/// </summary>
public sealed class RemovedVerification
{
    /// <summary>Verification mode: auto or manual</summary>
    public string Mode { get; init; }

    /// <summary>Person who verified the value (manual mode only)</summary>
    public string VerifiedBy { get; init; }

    /// <summary>Verification date in YYYY-MM-DD format</summary>
    public string VerifiedOn { get; init; }
}
