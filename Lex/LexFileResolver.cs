using System.Collections.Generic;
using System.IO;

namespace PayrollEngine.Lex;

/// <summary>
/// Resolves lex.json files from a file name or mask
/// </summary>
public static class LexFileResolver
{
    /// <summary>
    /// Resolve the lex.json files for a file name or mask
    /// </summary>
    /// <param name="fileMask">File name or mask, default lex.json</param>
    /// <param name="recursive">Search all subdirectories, otherwise the top directory only</param>
    public static List<string> Resolve(string fileMask, bool recursive)
    {
        fileMask ??= "lex.json";
        var searchOption = recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        // exact file
        if (File.Exists(fileMask))
        {
            return [Path.GetFullPath(fileMask)];
        }

        // file mask
        var fileInfo = new FileInfo(fileMask);
        var dir = string.IsNullOrEmpty(fileInfo.DirectoryName)
            ? Directory.GetCurrentDirectory()
            : fileInfo.DirectoryName;
        if (!Directory.Exists(dir))
        {
            return [];
        }
        var found = Directory.GetFiles(dir, fileInfo.Name, searchOption);
        var result = new List<string>(found.Length);
        foreach (var file in found)
        {
            var fullPath = Path.GetFullPath(file);
            // build output copies of lex.json (bin/obj) are not manifests
            if (searchOption == SearchOption.AllDirectories && IsBuildOutput(fullPath, dir))
            {
                continue;
            }
            result.Add(fullPath);
        }
        result.Sort(System.StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>
    /// Test if the file lies in a bin or obj folder below the search root
    /// </summary>
    private static bool IsBuildOutput(string fullPath, string searchRoot)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(searchRoot), fullPath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "bin", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segments[i], "obj", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
