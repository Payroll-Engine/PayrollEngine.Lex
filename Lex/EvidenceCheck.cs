using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Evidence files of a lex manifest: declaration, snapshot, git tracking and usage
/// </summary>
public static class EvidenceCheck
{
    internal static void CheckEvidence(Manifest manifest, string lexDirectory, CheckResult result)
    {
        var paths = new List<string>();
        var declared = CheckDeclaredFiles(manifest, lexDirectory, result, paths);
        CheckEntryFiles(manifest, lexDirectory, result, declared, paths);

        CheckVerificationEvidence(manifest, lexDirectory, result);
        var unused = UnusedEvidence(manifest, lexDirectory);
        result.UnusedEvidence = unused.Count;
        result.UnusedEvidenceSamples.AddRange(unused.Take(CheckResult.MaxSamples));

        CheckRepositoryState(lexDirectory, result, paths);

        var distinct = result.FileProblems.Distinct(StringComparer.Ordinal).Take(CheckResult.MaxSamples).ToList();
        result.FileProblems.Clear();
        result.FileProblems.AddRange(distinct);
    }

    /// <summary>
    /// Files declared in files[] must exist in the .lex folder
    /// </summary>
    /// <returns>Declared file names</returns>
    private static HashSet<string> CheckDeclaredFiles(Manifest manifest, string lexDirectory, CheckResult result, List<string> paths)
    {
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.FileName))
            {
                continue;
            }
            declared.Add(file.FileName);
            var path = Path.Combine(lexDirectory, file.FileName);
            if (!File.Exists(path))
            {
                result.FileProblems.Add($"declared file not in .lex: {file.FileName}");
            }
            else
            {
                paths.Add(path);
            }
        }
        return declared;
    }

    /// <summary>
    /// A documentRef must be declared and exist, a content file must exist
    /// </summary>
    private static void CheckEntryFiles(Manifest manifest, string lexDirectory, CheckResult result,
        HashSet<string> declared, List<string> paths)
    {
        foreach (var value in manifest.Values)
        {
            if (!string.IsNullOrWhiteSpace(value.DocumentRef))
            {
                if (!declared.Contains(value.DocumentRef))
                {
                    result.FileProblems.Add($"documentRef not in files[]: {value.DocumentRef}");
                }
                else if (!File.Exists(Path.Combine(lexDirectory, value.DocumentRef)))
                {
                    result.FileProblems.Add($"documentRef file missing: {value.DocumentRef}");
                }
            }
            if (value.HasContentFile)
            {
                var path = ContentHash.ResolvePath(lexDirectory, value.ContentFile);
                if (File.Exists(path))
                {
                    paths.Add(path);
                }
                else
                {
                    result.FileProblems.Add($"contentFile missing: {value.ContentFile}");
                }
            }
        }
    }

    /// <summary>
    /// Repository state of the evidence: untracked or ignored files are not part of the compliance record
    /// </summary>
    private static void CheckRepositoryState(string lexDirectory, CheckResult result, List<string> paths)
    {
        paths.Add(Path.Combine(lexDirectory, "lex.json"));
        if (result.HasSnapshot)
        {
            paths.Add(Path.Combine(lexDirectory, "snapshot.json"));
        }
        var untracked = GitList(lexDirectory, false);
        var ignored = GitList(lexDirectory, true);
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ignored.Contains(path))
            {
                result.FileProblems.Add($"git-ignored: {Path.GetFileName(path)}");
            }
            else if (untracked.Contains(path))
            {
                result.FileProblems.Add($"untracked in git: {Path.GetFileName(path)}");
            }
        }
    }

    /// <summary>
    /// Evidence files in the .lex folder that back no entry: a declared file is used by a documentRef, by a
    /// source URL of an entry, as contentFile or as evidence of a verification record. A file in the .lex folder
    /// without declaration in files[] is unused too (not frozen in the snapshot).
    /// An unused file is never read by LexVerifySource: its entries count as without local evidence.
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <returns>File names, undeclared files with the suffix " (not in files[])"</returns>
    public static List<string> UnusedEvidence(Manifest manifest, string lexDirectory)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        var contentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in manifest.Values)
        {
            if (!string.IsNullOrWhiteSpace(value.DocumentRef))
            {
                referenced.Add(value.DocumentRef);
            }
            if (!string.IsNullOrWhiteSpace(value.Verification?.Evidence))
            {
                referenced.Add(value.Verification.Evidence);
            }
            if (value.HasContentFile)
            {
                contentFiles.Add(Path.GetFullPath(ContentHash.ResolvePath(lexDirectory, value.ContentFile)));
            }
            urls.UnionWith(value.Urls);
        }

        var unused = new List<string>();
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files.Where(x => !string.IsNullOrWhiteSpace(x.FileName)))
        {
            declared.Add(file.FileName);
            if (referenced.Contains(file.FileName) || file.Urls.Any(urls.Contains) ||
                contentFiles.Contains(Path.GetFullPath(Path.Combine(lexDirectory, file.FileName))) ||
                !File.Exists(Path.Combine(lexDirectory, file.FileName)))
            {
                continue;
            }
            unused.Add(file.FileName);
        }
        if (Directory.Exists(lexDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(lexDirectory))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith('.') || declared.Contains(name) || contentFiles.Contains(Path.GetFullPath(path)) ||
                    string.Equals(name, "lex.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "snapshot.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                unused.Add(name + " (not in files[])");
            }
        }
        return unused;
    }

    /// <summary>
    /// The evidence of a verification record must be frozen in the snapshot
    /// </summary>
    private static void CheckVerificationEvidence(Manifest manifest, string lexDirectory, CheckResult result)
    {
        var evidences = manifest.Values
            .Select(x => x.Verification?.Evidence)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (evidences.Count == 0 || !result.HasSnapshot)
        {
            return;
        }

        Snapshot snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<Snapshot>(
                File.ReadAllText(Path.Combine(lexDirectory, "snapshot.json")), LexJson.SnapshotOptions);
        }
        catch (Exception exception)
        {
            result.FileProblems.Add("snapshot.json could not be read: " + exception.Message);
            return;
        }
        var frozen = (snapshot?.Files ?? [])
            .Select(x => x.FileName)
            .Where(x => x != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var evidence in evidences.Where(x => !frozen.Contains(x)))
        {
            result.FileProblems.Add($"verification evidence not in the snapshot: {evidence}");
        }
    }

    /// <summary>
    /// Lists untracked (or ignored) files below a directory using git. Returns an empty set without git or outside a repository.
    /// </summary>
    private static HashSet<string> GitList(string directory, bool ignored)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var info = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            info.ArgumentList.Add("ls-files");
            info.ArgumentList.Add("-o");
            if (ignored)
            {
                info.ArgumentList.Add("-i");
            }
            info.ArgumentList.Add("--exclude-standard");
            info.ArgumentList.Add(".");
            using var process = Process.Start(info);
            if (process == null)
            {
                return set;
            }
            var text = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return set;
            }
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                set.Add(Path.GetFullPath(Path.Combine(directory, line.Trim())));
            }
        }
        catch
        {
            // git is optional: without it the git state is not checked
        }
        return set;
    }

    // value comparison -------------------------------------------------------
}
