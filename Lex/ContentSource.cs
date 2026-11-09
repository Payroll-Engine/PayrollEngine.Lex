using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace PayrollEngine.Lex;

/// <summary>
/// Content file of a lex entry: a file or a file inside a ZIP archive (<c>archive.zip!entry.txt</c>).
/// An archive entry is read in place; the archive is the evidence copy and is not extracted.
/// </summary>
internal sealed class ContentSource
{
    /// <summary>Separator between the archive path and the entry name</summary>
    internal const char EntrySeparator = '!';

    /// <summary>Content file reference as written in lex.json</summary>
    internal string Reference { get; private init; }

    /// <summary>Full path of the file, or of the archive for an archive entry</summary>
    internal string FilePath { get; private init; }

    /// <summary>Name of the entry inside the archive, null for a plain file</summary>
    internal string EntryName { get; private init; }

    /// <summary>The content is an entry of a ZIP archive</summary>
    internal bool IsArchiveEntry => EntryName != null;

    /// <summary>File name of the file or archive</summary>
    internal string FileName => Path.GetFileName(FilePath);

    /// <summary>Name of the content: the entry name of an archive entry, else the file name</summary>
    internal string ContentName => IsArchiveEntry ? Path.GetFileName(EntryName) : FileName;

    /// <summary>
    /// Resolve a content file reference relative to the .lex folder
    /// </summary>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <param name="contentFile">Content file reference (file path or archive!entry)</param>
    internal static ContentSource Resolve(string lexDirectory, string contentFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFile);
        var (path, entry) = Split(contentFile);
        return new ContentSource
        {
            Reference = contentFile,
            FilePath = ContentHash.ResolvePath(lexDirectory, path),
            EntryName = entry
        };
    }

    /// <summary>
    /// Split a reference into the file path and the archive entry name (null without separator)
    /// </summary>
    internal static (string Path, string Entry) Split(string contentFile)
    {
        var index = contentFile.IndexOf(EntrySeparator);
        return index < 0
            ? (contentFile, null)
            : (contentFile[..index], contentFile[(index + 1)..].Replace('\\', '/'));
    }

    /// <summary>
    /// Reference with the same entry and the archive file name only, as recorded in the snapshot
    /// </summary>
    internal string LocalReference =>
        IsArchiveEntry ? FileName + EntrySeparator + EntryName : FileName;

    /// <summary>
    /// Why the content cannot be read, null when it exists
    /// </summary>
    internal string Problem()
    {
        if (!File.Exists(FilePath))
        {
            return IsArchiveEntry ? $"contentFile archive missing: {Reference}" : $"contentFile missing: {Reference}";
        }
        if (!IsArchiveEntry)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(EntryName))
        {
            return $"contentFile without archive entry name: {Reference}";
        }
        try
        {
            using var archive = ZipFile.OpenRead(FilePath);
            return Find(archive) == null ? $"contentFile entry missing in the archive: {Reference}" : null;
        }
        catch (InvalidDataException)
        {
            return $"contentFile archive is not a ZIP archive: {Reference}";
        }
    }

    /// <summary>
    /// Content bytes of the file or archive entry
    /// </summary>
    /// <exception cref="FileNotFoundException">File, archive or entry missing</exception>
    internal byte[] ReadAllBytes()
    {
        if (!IsArchiveEntry)
        {
            return File.ReadAllBytes(FilePath);
        }
        using var archive = ZipFile.OpenRead(FilePath);
        var entry = Find(archive) ?? throw new FileNotFoundException($"contentFile entry missing in the archive: {Reference}");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Content stream of the file or archive entry (in memory for an archive entry)
    /// </summary>
    internal Stream Open() =>
        IsArchiveEntry ? new MemoryStream(ReadAllBytes(), writable: false) : File.OpenRead(FilePath);

    /// <summary>
    /// Content text (UTF-8, a byte order mark selects the encoding)
    /// </summary>
    internal string ReadAllText()
    {
        using var reader = new StreamReader(Open());
        return reader.ReadToEnd();
    }

    /// <summary>
    /// SHA-256 of the content as lower-case hex: of the file, or of the uncompressed archive entry
    /// </summary>
    internal string Hash() =>
        IsArchiveEntry
            ? Convert.ToHexStringLower(SHA256.HashData(ReadAllBytes()))
            : ContentHash.FileHash(FilePath);

    /// <summary>
    /// The archive entry by its full name (forward slashes, case-sensitive)
    /// </summary>
    private ZipArchiveEntry Find(ZipArchive archive) =>
        archive.Entries.FirstOrDefault(x => string.Equals(x.FullName.Replace('\\', '/'), EntryName, StringComparison.Ordinal));
}
