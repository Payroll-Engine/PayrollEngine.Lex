using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PayrollEngine.Lex;

/// <summary>
/// Verifies a snapshot against its manifest and the files in the .lex folder
/// </summary>
public static class Verifier
{
    /// <summary>
    /// Verify the snapshot. Returns the list of differences (empty = snapshot is intact and current).
    /// </summary>
    /// <param name="manifest">Manifest (lex.json)</param>
    /// <param name="snapshot">Snapshot (snapshot.json)</param>
    /// <param name="lexDirectory">Directory containing lex.json and snapshot.json</param>
    public static List<string> Verify(Manifest manifest, Snapshot snapshot, string lexDirectory)
    {
        var errors = new List<string>();

        // header
        Compare(errors, "authority", manifest.Authority, snapshot.Authority);
        Compare(errors, "regulation", manifest.Regulation, snapshot.Regulation);
        Compare(errors, "validFrom", manifest.ValidFrom, snapshot.ValidFrom);
        Compare(errors, "provider", manifest.Provider, snapshot.Provider);

        // values: keys and inline content
        var snapshotValues = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var value in snapshot.Values ?? [])
        {
            if (value.Key != null)
            {
                snapshotValues[value.Key] = value;
            }
        }
        var manifestKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in manifest.Values)
        {
            if (value.Key == null)
            {
                continue;
            }
            manifestKeys.Add(value.Key);
            if (!snapshotValues.TryGetValue(value.Key, out var snapValue))
            {
                errors.Add($"Value '{value.Key}' is in lex.json but not in the snapshot");
                continue;
            }
            if (value.HasInlineContent && !string.Equals(value.Content, snapValue.Content, StringComparison.Ordinal))
            {
                errors.Add($"Value '{value.Key}': content differs from the snapshot");
            }
            VerifyVerification(errors, value, snapValue, snapshot);
        }
        foreach (var key in snapshotValues.Keys.Where(k => !manifestKeys.Contains(k)))
        {
            errors.Add($"Value '{key}' is in the snapshot but not in lex.json");
        }

        // snapshot files: present, same hash, same size
        foreach (var file in snapshot.Files ?? [])
        {
            var name = file.ContentFile ?? file.FileName;
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("Snapshot file entry without a file name");
                continue;
            }
            var path = Path.Combine(lexDirectory, name);
            if (!File.Exists(path))
            {
                errors.Add($"File '{name}' is in the snapshot but missing in the .lex folder");
                continue;
            }
            if (!string.Equals(ContentHash.FileHash(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"File '{name}': content differs from the snapshot (sha256)");
            }
            else if (file.FileSizeBytes != 0 && new FileInfo(path).Length != file.FileSizeBytes)
            {
                errors.Add($"File '{name}': size differs from the snapshot");
            }
        }

        // source hash recomputed from the files in the .lex folder, the same way LexSnapshot builds it
        var hash = ComputeSourceHash(manifest, lexDirectory, errors);
        if (hash != null && !string.Equals(hash, snapshot.SourceHash, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"sourceHash differs: snapshot {snapshot.SourceHash}, recomputed {hash}");
        }

        return errors;
    }

    private static void VerifyVerification(List<string> errors, Entry value, Entry snapValue, Snapshot snapshot)
    {
        var verification = value.Verification;
        if (verification == null)
        {
            if (snapValue.Verification != null)
            {
                errors.Add($"Value '{value.Key}': verification is in the snapshot but not in lex.json");
            }
            return;
        }
        if (!verification.SameAs(snapValue.Verification))
        {
            errors.Add($"Value '{value.Key}': verification differs from the snapshot");
        }
        // the evidence file itself is checked with the snapshot files (presence and hash)
        if (!string.IsNullOrWhiteSpace(verification.Evidence) &&
            !(snapshot.Files ?? []).Any(f => string.Equals(f.ContentFile ?? f.FileName, verification.Evidence, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Value '{value.Key}': verification evidence '{verification.Evidence}' is not in the snapshot");
        }
    }

    private static void Compare(List<string> errors, string field, string manifestValue, string snapshotValue)
    {
        if (!string.Equals(manifestValue, snapshotValue, StringComparison.Ordinal))
        {
            errors.Add($"Header '{field}' differs: lex.json '{manifestValue}', snapshot '{snapshotValue}'");
        }
    }

    /// <summary>
    /// Recompute the source hash. Returns null when a file needed for the hash is missing (reported in errors).
    /// Evidence copies in the .lex folder are the reference; an original outside .lex that differs from its copy is reported.
    /// </summary>
    private static string ComputeSourceHash(Manifest manifest, string lexDirectory, List<string> errors)
    {
        var input = new StringBuilder();
        var complete = true;

        foreach (var value in manifest.Values)
        {
            if (value.HasInlineContent)
            {
                input.Append(value.Content);
            }
            else if (value.HasContentFile)
            {
                var original = ContentHash.ResolvePath(lexDirectory, value.ContentFile);
                var local = Path.Combine(lexDirectory, Path.GetFileName(original));
                if (!File.Exists(local))
                {
                    errors.Add($"Value '{value.Key}': content file copy missing in the .lex folder: {Path.GetFileName(original)}");
                    complete = false;
                    continue;
                }
                var localHash = ContentHash.FileHash(local);
                input.Append(localHash);
                CheckOriginal(errors, $"Value '{value.Key}'", original, local, localHash);
            }
            else
            {
                continue;
            }

            if (value.Verification != null)
            {
                input.Append(value.Verification.HashInput());
            }
        }

        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.FileName))
            {
                continue;
            }
            var local = Path.Combine(lexDirectory, file.FileName);
            if (!File.Exists(local))
            {
                // LexSnapshot skips local-only files that do not exist; declared origins must exist
                if (!string.IsNullOrWhiteSpace(file.SourcePath) || !string.IsNullOrWhiteSpace(file.SourceUrl))
                {
                    errors.Add($"File '{file.FileName}' is declared in lex.json but missing in the .lex folder");
                    complete = false;
                }
                continue;
            }
            var localHash = ContentHash.FileHash(local);
            input.Append(localHash);
            if (!string.IsNullOrWhiteSpace(file.SourcePath))
            {
                CheckOriginal(errors, $"File '{file.FileName}'", ContentHash.ResolvePath(lexDirectory, file.SourcePath), local, localHash);
            }
        }

        return complete ? "sha256:" + ContentHash.StringHash(input.ToString()) : null;
    }

    private static void CheckOriginal(List<string> errors, string subject, string original, string local, string localHash)
    {
        if (string.IsNullOrWhiteSpace(original) || !File.Exists(original))
        {
            return;
        }
        if (string.Equals(Path.GetFullPath(original), Path.GetFullPath(local), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!string.Equals(ContentHash.FileHash(original), localHash, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{subject}: the original file differs from its copy in the .lex folder ({original})");
        }
    }
}
