namespace PayrollEngine.Lex;

/// <summary>
/// Manual verification of entries (verified by, date, evidence) or the second review of a manual verification
/// (reviewed by, date); the two are separate steps
/// </summary>
public sealed class AttestRequest
{
    /// <summary>Entry key or key pattern (<see cref="KeyPattern"/>)</summary>
    public string Key { get; init; }

    /// <summary>Person who verified the values against the evidence</summary>
    public string VerifiedBy { get; init; }

    /// <summary>Verification date yyyy-MM-dd (default: today)</summary>
    public string VerifiedOn { get; init; }

    /// <summary>Evidence document, declared in files[] and present in the .lex folder</summary>
    public string Evidence { get; init; }

    /// <summary>Second person who reviewed the manual verification (four-eyes)</summary>
    public string ReviewedBy { get; init; }

    /// <summary>Review date yyyy-MM-dd (default: today)</summary>
    public string ReviewedOn { get; init; }

    /// <summary>Replace an existing manual record or review</summary>
    public bool Overwrite { get; init; }

    /// <summary>Review mode: the second confirmation of an existing manual record</summary>
    public bool IsReview => !string.IsNullOrWhiteSpace(ReviewedBy);

    /// <summary>
    /// Parameter rules: a key is required; a review takes the reviewer only, a verification the person and
    /// the evidence; dates in the format yyyy-MM-dd
    /// </summary>
    /// <returns>Error message, null if valid</returns>
    public string Test()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return "Missing entry key or key pattern";
        }
        var verify = !string.IsNullOrWhiteSpace(VerifiedBy) || !string.IsNullOrWhiteSpace(Evidence) ||
                     !string.IsNullOrWhiteSpace(VerifiedOn);
        if (IsReview)
        {
            if (verify)
            {
                return "Verification and review are separate steps: use either VerifiedBy/Evidence or ReviewedBy";
            }
            return string.IsNullOrWhiteSpace(ReviewedOn) || EntryRules.IsDate(ReviewedOn)
                ? null
                : $"Invalid ReviewedOn '{ReviewedOn}', expected yyyy-MM-dd";
        }
        if (!string.IsNullOrWhiteSpace(ReviewedOn))
        {
            return "ReviewedOn requires ReviewedBy";
        }
        if (string.IsNullOrWhiteSpace(VerifiedBy) || string.IsNullOrWhiteSpace(Evidence))
        {
            return "Missing VerifiedBy and Evidence (or ReviewedBy for a review)";
        }
        return string.IsNullOrWhiteSpace(VerifiedOn) || EntryRules.IsDate(VerifiedOn)
            ? null
            : $"Invalid VerifiedOn '{VerifiedOn}', expected yyyy-MM-dd";
    }
}
