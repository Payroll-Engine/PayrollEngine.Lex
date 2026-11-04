using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PayrollEngine.Lex;

/// <summary>
/// Hash helpers shared by LexSnapshot, LexVerify and LexCheck.
/// The hash algorithm is part of the snapshot format and must not change.
/// </summary>
internal static class ContentHash
{
    /// <summary>
    /// SHA-256 of a file as lower-case hex string
    /// </summary>
    internal static string FileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// SHA-256 of a string (UTF-8) as lower-case hex string
    /// </summary>
    internal static string StringHash(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    /// <summary>
    /// Resolve a path relative to a base directory
    /// </summary>
    internal static string ResolvePath(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }
        return Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
    }
}
