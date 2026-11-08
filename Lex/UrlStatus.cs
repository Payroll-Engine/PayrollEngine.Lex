namespace PayrollEngine.Lex;

/// <summary>
/// Result of requesting one source URL
/// </summary>
internal enum UrlStatus
{
    /// <summary>The page answers (2xx or redirect to an answering page)</summary>
    Ok,

    /// <summary>The host blocks scripted requests (401, 403, 429) or fails temporarily (5xx, timeout): not verifiable</summary>
    Blocked,

    /// <summary>The page does not exist (404, 410) or the host is unknown</summary>
    Dead
}
