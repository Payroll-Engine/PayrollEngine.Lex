using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PayrollEngine.Lex;

/// <summary>
/// Content hash of a lex.json file: SHA-256 over the bytes of the file, as <c>sha256:</c> and lower-case hex.
/// A writer that read a manifest compares the hash before it writes (optimistic concurrency).
/// </summary>
public static class ManifestHash
{
    /// <summary>Prefix of the hash text</summary>
    public const string Prefix = "sha256:";

    /// <summary>
    /// Hash of the bytes of a manifest file
    /// </summary>
    /// <param name="bytes">File content</param>
    public static string OfBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Hash of a manifest text as it is written by the editor (UTF-8 without BOM)
    /// </summary>
    /// <param name="text">Manifest text</param>
    public static string OfText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return OfBytes(new UTF8Encoding(false).GetBytes(text));
    }

    /// <summary>
    /// Hash of a manifest file
    /// </summary>
    /// <param name="lexPath">Path of lex.json</param>
    public static string OfFile(string lexPath) =>
        OfBytes(File.ReadAllBytes(lexPath));
}
