using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Records the manual verification of manifest entries or the second review of a manual verification.
/// An automatic record is replaced, a manual record or a review only with overwrite.
/// </summary>
public static class AttestOperation
{
    /// <summary>
    /// Attest the entries matched by the key or key pattern and save the manifest when something changed
    /// </summary>
    /// <param name="lex">Path of lex.json or of its folder (default: lex.json or .lex/lex.json in the current folder)</param>
    /// <param name="request">Attestation</param>
    public static async Task<AttestationResult> AttestAsync(string lex, AttestRequest request)
    {
        var parameterError = request.Test();
        if (parameterError != null)
        {
            return new AttestationResult { Error = parameterError };
        }
        var lexPath = FileResolver.ResolveManifest(lex);
        if (lexPath == null)
        {
            return new AttestationResult { Error = $"lex.json not found: {lex ?? Directory.GetCurrentDirectory()}" };
        }
        var lexDirectory = Path.GetDirectoryName(lexPath)!;
        var editor = ManifestEditor.Parse(await File.ReadAllTextAsync(lexPath));

        var keys = editor.MatchKeys(request.Key);
        if (keys.Count == 0)
        {
            return new AttestationResult { LexPath = lexPath, Error = $"No entry matches {request.Key}" };
        }
        if (!request.IsReview)
        {
            if (!editor.HasFile(request.Evidence))
            {
                return new AttestationResult { LexPath = lexPath, Error = $"Evidence is not declared in files[] (LexFetch): {request.Evidence}" };
            }
            if (!File.Exists(Path.Combine(lexDirectory, request.Evidence)))
            {
                return new AttestationResult { LexPath = lexPath, Error = $"Evidence file not found in the .lex folder: {request.Evidence}" };
            }
        }

        var entries = keys.Select(key =>
        {
            var result = request.IsReview
                ? editor.SetReview(key, request.ReviewedBy, request.ReviewedOn ?? EntryRules.Today(), request.Overwrite)
                : editor.SetManualVerification(key, new Verification
                {
                    Mode = Verification.ModeManual,
                    VerifiedOn = request.VerifiedOn ?? EntryRules.Today(),
                    VerifiedBy = request.VerifiedBy,
                    Evidence = request.Evidence
                }, request.Overwrite);
            return new AttestEntryResult
            {
                Key = key,
                Result = result,
                ReviewNotRequired = request.IsReview && result == AttestResult.Written &&
                                    editor.SourceReview(key) != Source.ReviewFourEyes
            };
        }).ToList();

        if (editor.Changes > 0)
        {
            await editor.SaveAsync(lexPath);
        }
        return new AttestationResult { LexPath = lexPath, Entries = entries };
    }
}
