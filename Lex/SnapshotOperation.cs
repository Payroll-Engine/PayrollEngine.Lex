using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Snapshot of one manifest file with skip, overwrite and dry-run handling: an existing snapshot.json is kept
/// unless overwrite is set; the manifest is validated before the snapshot is written
/// </summary>
public static class SnapshotOperation
{
    /// <summary>File name of the snapshot</summary>
    public const string SnapshotFileName = "snapshot.json";

    /// <summary>
    /// Create the snapshot of a manifest file
    /// </summary>
    /// <param name="lexPath">Path of lex.json</param>
    /// <param name="targetPath">Folder of snapshot.json and the copied files, null or blank for the folder of lex.json</param>
    /// <param name="dryRun">Validate and report without writing anything</param>
    /// <param name="overwrite">Regenerate an existing snapshot.json; files with source URLs and an existing local copy
    /// are not downloaded again</param>
    /// <param name="loadLive">Loader of a live source URL (e.g. <see cref="SourceDocument.LoadLive"/>) for the text hash of
    /// the source URLs, null without live hashes; not called in dry-run mode</param>
    /// <param name="downloading">Called with the file name before a source file is downloaded; not called in dry-run mode</param>
    public static async Task<SnapshotFileResult> RunAsync(string lexPath, string targetPath = null, bool dryRun = false,
        bool overwrite = false, Func<string, Task<SourceDocument>> loadLive = null, Action<string> downloading = null)
    {
        var manifestDirectory = Path.GetDirectoryName(lexPath)!;
        var targetDirectory = string.IsNullOrWhiteSpace(targetPath) ? manifestDirectory : targetPath;
        var snapshotPath = Path.Combine(targetDirectory, SnapshotFileName);

        if (File.Exists(snapshotPath) && !overwrite)
        {
            return Result(SnapshotStatus.Skipped);
        }

        Manifest manifest;
        try
        {
            var json = await File.ReadAllTextAsync(lexPath);
            manifest = JsonSerializer.Deserialize<Manifest>(json, LexJson.StrictManifestOptions);
        }
        catch (Exception exception)
        {
            return Result(SnapshotStatus.Failed, parseError: exception.Message);
        }

        var errors = Validator.Validate(manifest, manifestDirectory);
        if (errors.Count > 0)
        {
            return new SnapshotFileResult
            {
                LexPath = lexPath, SnapshotPath = snapshotPath, DryRun = dryRun,
                Status = SnapshotStatus.Failed, ValidationErrors = errors
            };
        }

        try
        {
            if (!dryRun && !Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }
            var snapshot = await new SnapshotWriter().WriteAsync(manifest, manifestDirectory, targetDirectory, dryRun,
                (fileName, dryRunFile) =>
                {
                    if (!dryRunFile)
                    {
                        downloading?.Invoke(fileName);
                    }
                },
                keepLocalFiles: overwrite,
                loadLiveSource: loadLive);
            return Result(SnapshotStatus.Created, snapshot: snapshot);
        }
        catch (Exception exception)
        {
            return Result(SnapshotStatus.Failed, error: exception.Message);
        }

        SnapshotFileResult Result(SnapshotStatus status, string parseError = null, string error = null, Snapshot snapshot = null) => new()
        {
            LexPath = lexPath,
            SnapshotPath = snapshotPath,
            DryRun = dryRun,
            Status = status,
            ParseError = parseError,
            Error = error,
            Snapshot = snapshot
        };
    }
}
