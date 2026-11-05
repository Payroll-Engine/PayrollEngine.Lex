using System.IO.Compression;
using System.Text;

namespace PayrollEngine.Lex.Tests;

/// <summary>
/// Test documents created in memory
/// </summary>
internal static class TestDocuments
{
    /// <summary>
    /// ZIP archive with text entries, in the given order
    /// </summary>
    internal static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return stream.ToArray();
    }
}
