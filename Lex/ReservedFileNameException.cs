using System;

namespace PayrollEngine.Lex;

/// <summary>
/// The fetch target is reserved for the manifest (lex.json) or the snapshot (snapshot.json),
/// by its name (<see cref="Fetcher.IsReservedFileName"/>) or by its normalized path.
/// The parameter name is <c>fileName</c> and the message starts with <c>Reserved file name</c>
/// </summary>
public sealed class ReservedFileNameException : ArgumentException
{
    /// <summary>
    /// Create a new reserved file name exception
    /// </summary>
    /// <param name="fileName">The rejected file name, explicit or derived from the URL</param>
    public ReservedFileNameException(string fileName) :
        base($"Reserved file name (the manifest and the snapshot are no fetch target): {fileName}", nameof(fileName))
    {
        FileName = fileName;
    }

    /// <summary>The rejected file name, explicit or derived from the URL</summary>
    public string FileName { get; }
}
