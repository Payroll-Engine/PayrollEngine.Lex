using System.IO;

namespace PayrollEngine.Lex;

/// <summary>
/// The fetch target in the .lex folder is a symbolic link (or junction) that resolves outside the
/// real .lex folder, to the manifest (lex.json) or the snapshot (snapshot.json), or to no existing file,
/// or that cannot be resolved. The target was neither read nor written
/// </summary>
public sealed class FetchTargetLinkException : IOException
{
    /// <summary>
    /// Create a new fetch target link exception
    /// </summary>
    /// <param name="fileName">File name of the link in the .lex folder</param>
    /// <param name="resolvedPath">Final target of the link, or its immediate target if it cannot be resolved</param>
    /// <param name="reason">Why the target is refused</param>
    public FetchTargetLinkException(string fileName, string resolvedPath, string reason) :
        base($"Fetch target is a link that {reason}: {fileName} -> {resolvedPath}")
    {
        FileName = fileName;
        ResolvedPath = resolvedPath;
    }

    /// <summary>File name of the link in the .lex folder</summary>
    public string FileName { get; }

    /// <summary>Final target of the link (all links in the path resolved), or its immediate target
    /// if it cannot be resolved</summary>
    public string ResolvedPath { get; }
}
