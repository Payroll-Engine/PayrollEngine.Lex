namespace PayrollEngine.Lex;

/// <summary>
/// Write mode of the source verification (<see cref="VerifySourceOperation"/>)
/// </summary>
public enum SourceWriteMode
{
    /// <summary>Report only, the manifest is not changed</summary>
    ReportOnly,

    /// <summary>Write the found source location into entries without sourceLocation and the automatic
    /// verification record into entries whose numbers were all found</summary>
    WriteLocations,

    /// <summary>Like <see cref="WriteLocations"/>, and also replace the source locations written by the
    /// source verification (<see cref="SourceLocation.IsToolLocation"/>); locations written by a person are kept</summary>
    RefreshLocations
}
