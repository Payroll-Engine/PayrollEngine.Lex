using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PayrollEngine.Lex;

/// <summary>
/// Validates a <see cref="Manifest"/> for structural correctness
/// </summary>
public static class Validator
{
    /// <summary>
    /// Validates the manifest. Returns a list of error messages (empty = valid).
    /// </summary>
    /// <param name="manifest">Manifest to validate</param>
    /// <param name="manifestDirectory">Directory containing lex.json (used to resolve relative ContentFile paths)</param>
    public static List<string> Validate(Manifest manifest, string manifestDirectory)
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

        ValidateLanguages(errors, manifest.Languages);

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

            if (value.SourceQuote != null && string.IsNullOrWhiteSpace(value.SourceQuote))
            {
                errors.Add($"Value '{value.Key}': sourceQuote is empty");
            }

            if (value.ReferenceYear is < 1990 or > 2100)
            {
                errors.Add($"Value '{value.Key}': referenceYear {value.ReferenceYear} is not a year from 1990 to 2100");
            }
        }

        // exclusions
        foreach (var exclusion in manifest.Exclusions)
        {
            if (exclusion.Kind != Exclusion.KindCoverage && exclusion.Kind != Exclusion.KindSourceLocation &&
                exclusion.Kind != Exclusion.KindOrphan && exclusion.Kind != Exclusion.KindAutoVerification &&
                exclusion.Kind != Exclusion.KindDerived && exclusion.Kind != Exclusion.KindNotApplicable)
            {
                errors.Add($"Exclusion '{exclusion.Pattern}': kind must be '{Exclusion.KindCoverage}', '{Exclusion.KindSourceLocation}', " +
                           $"'{Exclusion.KindOrphan}', '{Exclusion.KindAutoVerification}', '{Exclusion.KindDerived}' " +
                           $"or '{Exclusion.KindNotApplicable}'");
            }
            if (string.IsNullOrWhiteSpace(exclusion.Pattern))
            {
                errors.Add("An exclusion is missing the required 'pattern' field");
            }
            if (string.IsNullOrWhiteSpace(exclusion.Reason))
            {
                errors.Add($"Exclusion '{exclusion.Pattern}': the 'reason' is mandatory" + exclusion.Kind switch
                {
                    Exclusion.KindDerived => " (formula and legal reference)",
                    Exclusion.KindNotApplicable => " (why the rule does not apply)",
                    _ => string.Empty
                });
            }
            if (exclusion.Kind == Exclusion.KindDerived && !string.IsNullOrWhiteSpace(exclusion.Pattern) &&
                seenKeys.Contains(exclusion.Pattern.Trim()))
            {
                errors.Add($"Exclusion (derived) '{exclusion.Pattern}' addresses the whole entry: a derived exclusion needs a property " +
                           $"(Lookup.Key.Property); to verify the whole entry manually use '{Exclusion.KindAutoVerification}'");
            }
            if (exclusion.Kind == Exclusion.KindNotApplicable && !string.IsNullOrWhiteSpace(exclusion.Pattern) &&
                seenKeys.Contains(exclusion.Pattern.Trim()))
            {
                errors.Add($"Exclusion (notApplicable) '{exclusion.Pattern}' addresses the whole entry: a notApplicable exclusion " +
                           "needs a property with the value 0 (Lookup.Key.Property)");
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

        // source review and official sources
        var seenUrls = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var source in manifest.Sources)
        {
            if (source.Review != null && source.Review != Source.ReviewSingleAuthor &&
                source.Review != Source.ReviewFourEyes)
            {
                errors.Add($"Source '{source.Url}': review must be '{Source.ReviewSingleAuthor}' or '{Source.ReviewFourEyes}'");
            }
            if (string.IsNullOrWhiteSpace(source.Url))
            {
                continue;
            }
            if (seenUrls.TryGetValue(source.Url, out var official) && official != source.IsOfficial)
            {
                errors.Add($"Source '{source.Url}' is declared as official and as secondary source");
            }
            seenUrls.TryAdd(source.Url, source.IsOfficial);
        }

        // verification
        foreach (var value in manifest.Values)
        {
            if (value.Verification != null && !string.IsNullOrWhiteSpace(value.Key))
            {
                ValidateVerification(errors, value.Key, value.Verification, declaredFileNames);
                if (value.Open)
                {
                    errors.Add($"Value '{value.Key}': an open entry must not have a verification record");
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
            else if (!file.Urls.Any())
            {
                var localPath = Path.Combine(manifestDirectory, file.FileName);
                if (!File.Exists(localPath))
                {
                    errors.Add($"File '{file.FileName}': no sourcePath, no sourceUrls, and file not found in .lex folder: {localPath}");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Languages of the manifest: language codes without duplicates; the language files are resolved by
    /// LexVerifySource (see <see cref="LanguageSet"/>), the folder of additional language files is given there
    /// </summary>
    private static void ValidateLanguages(List<string> errors, List<string> languages)
    {
        if (languages == null)
        {
            return;
        }
        var invalid = languages.Where(x => !LanguageProfile.IsLanguageCode(x)).ToList();
        if (invalid.Count > 0)
        {
            errors.Add($"Invalid language code(s) {string.Join(", ", invalid.Select(x => $"'{x}'"))} — " +
                       "expected two or three lower-case letters (e.g. de, fr)");
            return;
        }
        var duplicates = languages.GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
        if (duplicates.Count > 0)
        {
            errors.Add($"Duplicate language(s): {string.Join(", ", duplicates)}");
        }
    }

    private static void ValidateVerification(List<string> errors, string key, Verification verification,
        HashSet<string> declaredFileNames)
    {
        var subject = $"Value '{key}': verification";
        var manual = verification.Mode == Verification.ModeManual;
        if (!manual && verification.Mode != Verification.ModeAuto)
        {
            errors.Add($"{subject} mode must be '{Verification.ModeAuto}' or '{Verification.ModeManual}'");
        }

        ValidateDate(errors, subject, "verifiedOn", verification.VerifiedOn, required: true);

        if (manual)
        {
            if (string.IsNullOrWhiteSpace(verification.VerifiedBy))
            {
                errors.Add($"{subject} missing required field: verifiedBy");
            }
            if (string.IsNullOrWhiteSpace(verification.Evidence))
            {
                errors.Add($"{subject} missing required field: evidence");
            }
            if (verification.Method != null)
            {
                errors.Add($"{subject} method is only allowed in mode '{Verification.ModeAuto}'");
            }
            if (verification.Derived != null)
            {
                errors.Add($"{subject} derived is only allowed in mode '{Verification.ModeAuto}'");
            }
        }
        else if (verification.Mode == Verification.ModeAuto)
        {
            if (string.IsNullOrWhiteSpace(verification.Method))
            {
                errors.Add($"{subject} missing required field: method");
            }
            if (verification.VerifiedBy != null)
            {
                errors.Add($"{subject} verifiedBy is only allowed in mode '{Verification.ModeManual}'");
            }
        }

        if (!string.IsNullOrWhiteSpace(verification.Evidence) && !declaredFileNames.Contains(verification.Evidence))
        {
            errors.Add($"{subject} evidence '{verification.Evidence}' not found in files[] array");
        }

        if ((verification.ReviewedBy == null) != (verification.ReviewedOn == null))
        {
            errors.Add($"{subject} reviewedBy and reviewedOn must be set together");
        }
        ValidateDate(errors, subject, "reviewedOn", verification.ReviewedOn, required: false);
    }

    private static void ValidateDate(List<string> errors, string subject, string field, string value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add($"{subject} missing required field: {field}");
            }
            return;
        }
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", out _))
        {
            errors.Add($"{subject} invalid {field} format '{value}' — expected YYYY-MM-DD");
        }
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
