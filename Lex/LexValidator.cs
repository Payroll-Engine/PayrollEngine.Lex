using System;
using System.Collections.Generic;
using System.IO;

namespace PayrollEngine.Lex;

/// <summary>
/// Validates a <see cref="LexManifest"/> for structural correctness
/// </summary>
public static class LexValidator
{
    /// <summary>
    /// Validates the manifest. Returns a list of error messages (empty = valid).
    /// </summary>
    /// <param name="manifest">Manifest to validate</param>
    /// <param name="manifestDirectory">Directory containing lex.json (used to resolve relative ContentFile paths)</param>
    public static List<string> Validate(LexManifest manifest, string manifestDirectory)
    {
        var errors = new List<string>();

        // required root fields
        if (string.IsNullOrWhiteSpace(manifest.Authority))
        {
            errors.Add("Missing required field: authority");
        }
        if (string.IsNullOrWhiteSpace(manifest.Regulation))
        {
            errors.Add("Missing required field: regulation");
        }
        if (string.IsNullOrWhiteSpace(manifest.ValidFrom))
        {
            errors.Add("Missing required field: validFrom");
        }
        else if (!DateOnly.TryParseExact(manifest.ValidFrom, "yyyy-MM-dd", out _))
        {
            errors.Add($"Invalid validFrom format '{manifest.ValidFrom}' — expected YYYY-MM-DD");
        }

        // values
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in manifest.Values)
        {
            if (string.IsNullOrWhiteSpace(value.Key))
            {
                errors.Add("A value entry is missing the required 'key' field");
                continue;
            }

            if (!seenKeys.Add(value.Key))
            {
                errors.Add($"Duplicate value key: '{value.Key}'");
            }

            var hasContent = value.HasInlineContent;
            var hasContentFile = value.HasContentFile;

            if (!hasContent && !hasContentFile)
            {
                errors.Add($"Value '{value.Key}': must have either 'content' or 'contentFile'");
            }
            else if (hasContent && hasContentFile)
            {
                errors.Add($"Value '{value.Key}': 'content' and 'contentFile' are mutually exclusive");
            }
            else if (hasContentFile)
            {
                var resolved = ResolvePath(manifestDirectory, value.ContentFile);
                if (!File.Exists(resolved))
                {
                    errors.Add($"Value '{value.Key}': contentFile not found: {resolved}");
                }
            }
        }

        // exclusions
        foreach (var exclusion in manifest.Exclusions)
        {
            if (exclusion.Kind != LexExclusion.KindCoverage && exclusion.Kind != LexExclusion.KindSourceLocation &&
                exclusion.Kind != LexExclusion.KindOrphan)
            {
                errors.Add($"Exclusion '{exclusion.Pattern}': kind must be '{LexExclusion.KindCoverage}', '{LexExclusion.KindSourceLocation}' or '{LexExclusion.KindOrphan}'");
            }
            if (string.IsNullOrWhiteSpace(exclusion.Pattern))
            {
                errors.Add("An exclusion is missing the required 'pattern' field");
            }
            if (string.IsNullOrWhiteSpace(exclusion.Reason))
            {
                errors.Add($"Exclusion '{exclusion.Pattern}': the 'reason' is mandatory");
            }
        }

        // files — collect names first for documentRef cross-validation
        var declaredFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (!string.IsNullOrWhiteSpace(file.FileName))
            {
                declaredFileNames.Add(file.FileName);
            }
        }

        // documentRef validation
        foreach (var value in manifest.Values)
        {
            if (string.IsNullOrWhiteSpace(value.DocumentRef))
            {
                continue;
            }
            if (!declaredFileNames.Contains(value.DocumentRef))
            {
                errors.Add($"Value '{value.Key}': documentRef '{value.DocumentRef}' not found in files[] array");
            }
            else
            {
                var docPath = Path.Combine(manifestDirectory, value.DocumentRef);
                if (!File.Exists(docPath))
                {
                    errors.Add($"Value '{value.Key}': documentRef file not found in .lex folder: {docPath}");
                }
            }
        }

        // files
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.FileName))
            {
                errors.Add("A file entry is missing the required 'fileName' field");
                continue;
            }

            if (!seenFileNames.Add(file.FileName))
            {
                errors.Add($"Duplicate file name: '{file.FileName}'");
            }

            if (!string.IsNullOrWhiteSpace(file.SourcePath))
            {
                var resolved = ResolvePath(manifestDirectory, file.SourcePath);
                if (!File.Exists(resolved))
                {
                    errors.Add($"File '{file.FileName}': sourcePath not found: {resolved}");
                }
            }
            else if (string.IsNullOrWhiteSpace(file.SourceUrl))
            {
                var localPath = Path.Combine(manifestDirectory, file.FileName);
                if (!File.Exists(localPath))
                {
                    errors.Add($"File '{file.FileName}': no sourcePath, no sourceUrl, and file not found in .lex folder: {localPath}");
                }
            }
        }

        return errors;
    }

    private static string ResolvePath(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }
        return Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
    }
}
