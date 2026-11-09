using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Reads and writes ZIP archives of the Regulation folder. An archive is written deterministically: entries
/// sorted by name, fixed timestamp and compression, so the same content gives the same bytes and git sees
/// no changes without a content change.
/// </summary>
internal static class ArchiveWriter
{
    /// <summary>Timestamp of all entries (start of the ZIP date range)</summary>
    private static readonly DateTimeOffset EntryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Entries of an archive with their uncompressed content, ordered by name (ordinal)
    /// </summary>
    /// <exception cref="InvalidDataException">The file is no valid archive</exception>
    internal static List<(string Name, byte[] Content)> ReadEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = new List<(string Name, byte[] Content)>();
        // folder entries have no content
        foreach (var entry in archive.Entries.Where(x => !x.FullName.EndsWith('/')).OrderBy(x => x.FullName, StringComparer.Ordinal))
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            entries.Add((entry.FullName, buffer.ToArray()));
        }
        return entries;
    }

    /// <summary>
    /// Archive bytes of the entries
    /// </summary>
    internal static byte[] Write(IEnumerable<(string Name, byte[] Content)> entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = EntryTime;
                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Archive bytes with replaced entries; the other entries are kept
    /// </summary>
    /// <param name="path">Existing archive</param>
    /// <param name="replacements">New content by entry name</param>
    internal static byte[] Replace(string path, IReadOnlyDictionary<string, byte[]> replacements)
    {
        var entries = ReadEntries(path)
            .Select(x => replacements.TryGetValue(x.Name, out var content) ? (x.Name, content) : x)
            .ToList();
        return Write(entries);
    }
}
