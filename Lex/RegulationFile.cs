namespace PayrollEngine.Lex;

/// <summary>
/// Regulation JSON file with its content (without byte order mark): a JSON file or a JSON entry of a ZIP archive
/// </summary>
internal sealed class RegulationFile
{
    /// <summary>Full path of the file, or of the archive for an archive entry</summary>
    internal string Path { get; init; }
    /// <summary>Name of the JSON entry inside the archive, null for a JSON file</summary>
    internal string EntryName { get; init; }
    /// <summary>Byte order mark of the file, empty without</summary>
    internal byte[] Bom { get; init; }
    /// <summary>File content without byte order mark; token offsets refer to it</summary>
    internal byte[] Content { get; init; }
    /// <summary>The file holds lookups only (besides the tenant and regulation header): it may become an archive</summary>
    internal bool LookupsOnly { get; set; } = true;

    /// <summary>The file is a JSON entry of a ZIP archive</summary>
    internal bool IsArchiveEntry => EntryName != null;

    /// <summary>File name for reports: archive and entry name for an archive entry</summary>
    internal string DisplayName =>
        IsArchiveEntry
            ? System.IO.Path.GetFileName(Path) + ContentSource.EntrySeparator + EntryName
            : System.IO.Path.GetFileName(Path);
}
