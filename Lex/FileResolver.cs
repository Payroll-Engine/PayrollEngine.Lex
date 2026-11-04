using System.Collections.Generic;
using System.IO;

namespace PayrollEngine.Lex;

/// <summary>
/// Resolves lex.json files from a file name or mask
/// </summary>
public static class FileResolver
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
    /// Resolve a single manifest: a lex.json path or a folder containing lex.json or .lex/lex.json.
    /// Without path the current folder is used.
    /// </summary>
    /// <param name="path">Manifest path or folder</param>
    /// <returns>Full path of lex.json, null if not found</returns>
    public static string ResolveManifest(string path)
    {
        var target = string.IsNullOrWhiteSpace(path) ? Directory.GetCurrentDirectory() : path;
        if (File.Exists(target))
        {
            return Path.GetFullPath(target);
        }
        if (!Directory.Exists(target))
        {
            return null;
        }
        foreach (var candidate in new[] { Path.Combine(target, "lex.json"), Path.Combine(target, ".lex", "lex.json") })
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    /// <summary>
    /// Resolve the lex.json files in all repository folders below a root folder
    /// </summary>
    /// <param name="root">Root folder containing the repository folders; without root the current folder is used</param>
    /// <param name="repoMask">Mask of the repository folder names below the root, default *</param>
    /// <param name="fileMask">File name or mask relative to each repository folder, default lex.json</param>
    /// <param name="recursive">Search all subdirectories of each repository folder</param>
    public static List<string> Resolve(string root, string repoMask, string fileMask, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return Resolve(fileMask, recursive);
        }
        if (!Directory.Exists(root))
        {
            return [];
        }
        fileMask ??= "lex.json";
        var repositories = Directory.GetDirectories(root, string.IsNullOrWhiteSpace(repoMask) ? "*" : repoMask);
        System.Array.Sort(repositories, System.StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var repository in repositories)
        {
            result.AddRange(Resolve(Path.Combine(repository, fileMask), recursive));
        }
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
