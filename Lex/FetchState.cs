namespace PayrollEngine.Lex;

/// <summary>
/// State of a fetched evidence file
/// </summary>
public enum FetchState
{
    /// <summary>The file was created</summary>
    Created,

    /// <summary>The existing file has the same content</summary>
    Unchanged,

    /// <summary>The existing file was replaced</summary>
    Replaced,

    /// <summary>The existing file differs and was kept (requires overwrite)</summary>
    Differs
}
