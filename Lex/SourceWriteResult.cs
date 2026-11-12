namespace PayrollEngine.Lex;

/// <summary>
/// Result of writing the source verification into a manifest
/// </summary>
public sealed class SourceWriteResult
{
    /// <summary>Source locations written</summary>
    public int Locations { get; internal set; }

    /// <summary>Automatic verification records written</summary>
    public int Verifications { get; internal set; }

    /// <summary>The manifest file was saved (at least one location or record written)</summary>
    public bool Saved { get; internal set; }

    /// <summary>Number of changes of the manifest</summary>
    public int Changes => Locations + Verifications;
}
