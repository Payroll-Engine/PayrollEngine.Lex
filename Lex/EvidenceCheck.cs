using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Evidence files of a lex manifest: declaration, snapshot, git tracking and usage
/// </summary>
public static class EvidenceCheck
{
    /// <summary>
    /// Checks the evidence files: declared and present, frozen in the snapshot, used and tracked in git
    /// </summary>
    internal static void CheckEvidence(Manifest manifest, string lexDirectory, CheckResult result)
    {
        // existing evidence files, checked for their git state at the end
        var paths = new List<string>();
        var declared = CheckDeclaredFiles(manifest, lexDirectory, result, paths);
        CheckEntryFiles(manifest, lexDirectory, result, declared, paths);

        CheckVerificationEvidence(manifest, lexDirectory, result);
        var unused = UnusedEvidence(manifest, lexDirectory);
        result.UnusedEvidence = unused.Count;
        result.UnusedEvidenceSamples.AddRange(unused.Take(CheckResult.MaxSamples));

        CheckRepositoryState(lexDirectory, result, paths);

        // a file can be reported by several checks: once is enough
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
    /// A documentRef must be declared and exist, a content file (or archive entry) and a content map must exist
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
                var source = ContentSource.Resolve(lexDirectory, value.ContentFile);
                if (File.Exists(source.FilePath))
                {
                    paths.Add(source.FilePath);
                }
                var problem = source.Problem();
                if (problem != null)
                {
                    result.FileProblems.Add(problem);
                }
            }
            if (value.HasContentMap)
            {
                var path = ContentHash.ResolvePath(lexDirectory, value.ContentMap);
                if (File.Exists(path))
                {
                    paths.Add(path);
                }
                else
                {
                    result.FileProblems.Add($"contentMap missing: {value.ContentMap}");
                }
            }
        }
    }

    /// <summary>
    /// Repository state of the evidence: untracked or ignored files are not part of the compliance record
    /// </summary>
    private static void CheckRepositoryState(string lexDirectory, CheckResult result, List<string> paths)
    {
        // the manifest and the snapshot are part of the compliance record too
        paths.Add(Path.Combine(lexDirectory, "lex.json"));
        if (result.HasSnapshot)
        {
            paths.Add(Path.Combine(lexDirectory, "snapshot.json"));
        }
        // both queries are independent: run them side by side
        var untrackedTask = Task.Run(() => GitList(lexDirectory, false));
        var ignoredTask = Task.Run(() => GitList(lexDirectory, true));
        Task.WaitAll(untrackedTask, ignoredTask);
        var untracked = untrackedTask.Result.Files;
        var ignored = ignoredTask.Result.Files;
        // without git (or outside a repository) nothing is listed: say so instead of passing silently
        var problem = untrackedTask.Result.Problem ?? ignoredTask.Result.Problem;
        result.EvidenceVersioningChecked = problem == null;
        if (problem != null)
        {
            result.Warnings.Add($"{problem}: evidence versioning not checked");
        }
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
    /// source URL of an entry, as contentFile (or archive of a contentFile entry), as contentMap or as evidence
    /// of a verification record. A file in the .lex folder without declaration in files[] is unused too
    /// (not frozen in the snapshot).
    /// An unused file is never read by LexVerifySource: its entries count as without local evidence.
    /// </summary>
    /// <param name="manifest">Manifest</param>
    /// <param name="lexDirectory">Folder of lex.json</param>
    /// <returns>File names, undeclared files with the suffix " (not in files[])"</returns>
    public static List<string> UnusedEvidence(Manifest manifest, string lexDirectory)
    {
        // collect all uses of evidence files from the entries
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
                contentFiles.Add(Path.GetFullPath(ContentSource.Resolve(lexDirectory, value.ContentFile).FilePath));
            }
            if (value.HasContentMap)
            {
                contentFiles.Add(Path.GetFullPath(ContentHash.ResolvePath(lexDirectory, value.ContentMap)));
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
                // a missing declared file is reported by CheckDeclaredFiles, not as unused
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
                // hidden files, the manifest and the snapshot are no evidence
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
        // file names frozen by LexSnapshot
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
    /// Lists untracked (or ignored) files below a directory using git. Without git or outside a repository the
    /// file set is empty and the problem says why (the check reports it as a warning).
    /// </summary>
    private static GitListing GitList(string directory, bool ignored)
    {
        var info = new ProcessStartInfo("git");
        // git ls-files -o [-i] --exclude-standard: untracked (or ignored) files below the folder
        info.ArgumentList.Add("ls-files");
        info.ArgumentList.Add("-o");
        if (ignored)
        {
            info.ArgumentList.Add("-i");
        }
        info.ArgumentList.Add("--exclude-standard");
        info.ArgumentList.Add(".");
        return RunListing(info, directory, GitTimeout);
    }

    /// <summary>Result of a file listing: the files, or the reason why nothing could be listed</summary>
    internal sealed record GitListing(HashSet<string> Files, string Problem);

    /// <summary>Hard limit of a git call: a stalled child process must not stall the check</summary>
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs a file listing command (one path per line) in a directory. Returns an empty set when the command
    /// is not available, fails or does not finish within the timeout (the process is then ended).
    /// The child process neither inherits nor reads the standard input of the host (an MCP server talks over it),
    /// and standard output and standard error are read at the same time.
    /// </summary>
    internal static HashSet<string> ListFiles(ProcessStartInfo info, string directory, TimeSpan timeout) =>
        RunListing(info, directory, timeout).Files;

    /// <summary>
    /// Like <see cref="ListFiles"/>, with the reason when nothing could be listed: command not available, folder not
    /// in a repository, timeout. Never throws.
    /// </summary>
    internal static GitListing RunListing(ProcessStartInfo info, string directory, TimeSpan timeout)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return new GitListing(set, "folder not found, git state not read");
        }
        try
        {
            info.WorkingDirectory = directory;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            using var process = Process.Start(info);
            if (process == null)
            {
                return new GitListing(set, "git not available");
            }
            // nothing to send: close the input at once
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // the process has already ended
            }
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // already ended
                }
                return new GitListing(set, $"git did not answer within {(int)timeout.TotalSeconds} seconds");
            }
            // the exit is signalled: drain the pipes
            Task.WaitAll([outputTask, errorTask], timeout);
            if (process.ExitCode != 0)
            {
                var error = errorTask.IsCompletedSuccessfully ? errorTask.Result : string.Empty;
                return new GitListing(set, error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                    ? "not inside a git repository"
                    : $"git failed with exit code {process.ExitCode}");
            }
            if (!outputTask.IsCompletedSuccessfully)
            {
                return new GitListing(set, "git output could not be read");
            }
            var text = outputTask.Result;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                set.Add(Path.GetFullPath(Path.Combine(directory, line.Trim())));
            }
            return new GitListing(set, null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // the executable is not found
            return new GitListing(set, "git not available");
        }
        catch (Exception exception)
        {
            // git is optional: it never stops the check
            return new GitListing(set, $"git could not be run ({exception.GetType().Name})");
        }
    }
}
