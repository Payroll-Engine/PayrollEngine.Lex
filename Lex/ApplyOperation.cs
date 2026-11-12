using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Applies a manifest file to its data regulation: the manifest is read and validated, then the lex values
/// are written into the regulation lookups (<see cref="Applier"/>)
/// </summary>
public static class ApplyOperation
{
    /// <summary>Next step after applied changes</summary>
    public const string CheckHint = "Check the regulation (LexCheck) and run the regulation tests.";

    /// <summary>
    /// Apply a manifest file; a manifest that cannot be read or is invalid gives an error and writes nothing
    /// </summary>
    /// <param name="lexPath">Path of lex.json</param>
    /// <param name="dryRun">Report the differences without writing</param>
    public static async Task<ApplyResult> ApplyFileAsync(string lexPath, bool dryRun)
    {
        Manifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(lexPath), LexJson.ManifestOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return new ApplyResult { LexPath = lexPath, Error = "lex.json could not be read: " + exception.Message };
        }
        if (manifest == null)
        {
            return new ApplyResult { LexPath = lexPath, Error = "lex.json is empty" };
        }
        var errors = Validator.Validate(manifest, Path.GetDirectoryName(lexPath)!);
        if (errors.Count > 0)
        {
            return new ApplyResult { LexPath = lexPath, Error = $"lex.json is invalid: {errors[0]}" };
        }
        return Applier.Apply(manifest, lexPath, dryRun);
    }

    /// <summary>
    /// Hint for a regulation JSON file replaced by a ZIP archive (<see cref="ApplyResult.ReplacedFiles"/>):
    /// its references are updated by hand
    /// </summary>
    /// <param name="replaced">Replaced file, <c>old.json -> new.zip</c></param>
    public static string ReplacedFileHint(string replaced) =>
        $"{replaced}: update installFiles (regulation-package.json), the project file and Setup.pecmd";

    /// <summary>
    /// Hints for all regulation JSON files of the result replaced by a ZIP archive
    /// </summary>
    /// <param name="result">Apply result</param>
    public static List<string> ReplacedFileHints(ApplyResult result) =>
        result.ReplacedFiles.Select(ReplacedFileHint).ToList();
}
