using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Generates snapshot.json from a validated <see cref="Manifest"/> and copies referenced files.
/// Downloads files from their sourceUrls when no local sourcePath is provided.
/// </summary>
public sealed class SnapshotWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string ToolVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0";

    private static readonly HttpClient HttpClient = new();

    private const int MaxParallelLiveSources = 6;

    /// <summary>
    /// Creates the snapshot: downloads/copies files and writes snapshot.json
    /// </summary>
    /// <param name="manifest">Validated manifest</param>
    /// <param name="manifestDirectory">Directory of lex.json (source root)</param>
    /// <param name="targetDirectory">Output directory (default: same as manifestDirectory)</param>
    /// <param name="dryRun">When true, reports without writing anything</param>
    /// <param name="writeFile">Optional callback with the file name and the dry-run flag, invoked for every
    /// file downloaded from its source URL, in dry-run mode as well</param>
    /// <param name="keepLocalFiles">When true, a file with a source URL whose copy already exists in the
    /// manifest directory is taken from the copy and not downloaded again</param>
    /// <param name="loadLiveSource">Optional loader of a live source URL (e.g. <see cref="SourceDocument.LoadLive"/>);
    /// when set, the snapshot records the text hash of every source URL. Not called in dry-run mode.</param>
    /// <returns>Written snapshot (for reporting)</returns>
    public async Task<Snapshot> WriteAsync(
        Manifest manifest,
        string manifestDirectory,
        string targetDirectory,
        bool dryRun,
        Action<string, bool> writeFile = null,
        bool keepLocalFiles = false,
        Func<string, Task<SourceDocument>> loadLiveSource = null)
    {
        var snapshotFiles = new List<SnapshotFile>();
        var hashInput = new StringBuilder();

        // process values with contentFile
        var resolvedValues = new List<Entry>();
        foreach (var value in manifest.Values)
        {
            if (value.HasInlineContent)
            {
                hashInput.Append(value.Content);
                resolvedValues.Add(value);
            }
            else
            {
                var sourcePath = ResolvePath(manifestDirectory, value.ContentFile);
                var localName = Path.GetFileName(sourcePath);
                var targetPath = Path.Combine(targetDirectory, localName);

                var fileHash = ComputeFileHash(sourcePath);
                hashInput.Append(fileHash);

                if (!dryRun && !string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                }

                var fileInfo = new FileInfo(sourcePath);
                snapshotFiles.Add(new SnapshotFile
                {
                    FileName = localName,
                    ContentFile = localName,
                    ContentType = GetContentType(localName),
                    Sha256 = fileHash,
                    FileSizeBytes = fileInfo.Length,
                    Description = value.Description
                });

                resolvedValues.Add(new Entry
                {
                    Key = value.Key,
                    ContentFile = localName,
                    DocumentRef = value.DocumentRef,
                    Unit = value.Unit,
                    SourceUrls = value.SourceUrls,
                    SourceLocation = value.SourceLocation,
                    SourceQuote = value.SourceQuote,
                    LegalReference = value.LegalReference,
                    Description = value.Description,
                    Verification = value.Verification,
                    Open = value.Open
                });
            }

            hashInput.Append(value.QuoteHashInput());
            if (value.Verification != null)
            {
                hashInput.Append(value.Verification.HashInput());
            }
            if (value.Open)
            {
                hashInput.Append(Entry.OpenHashInput);
            }
        }

        // process explicit files[]
        foreach (var file in manifest.Files)
        {
            var localName = file.FileName;
            var targetPath = Path.Combine(targetDirectory, localName);

            if (!string.IsNullOrWhiteSpace(file.SourcePath))
            {
                var sourcePath = ResolvePath(manifestDirectory, file.SourcePath);
                if (!dryRun && !string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                }

                AddFileToSnapshot(snapshotFiles, hashInput, sourcePath, localName, file.Description);
            }
            else if (file.Urls.Any() && keepLocalFiles &&
                     File.Exists(Path.Combine(manifestDirectory, localName)))
            {
                var localPath = Path.Combine(manifestDirectory, localName);
                if (!dryRun && !string.Equals(manifestDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(localPath, targetPath, overwrite: true);
                }
                AddFileToSnapshot(snapshotFiles, hashInput, localPath, localName, file.Description);
            }
            else if (file.Urls.Any())
            {
                var localPath = Path.Combine(manifestDirectory, localName);

                writeFile?.Invoke(localName, dryRun);
                if (!dryRun)
                {
                    var bytes = await DownloadAsync(file.Urls.ToList());
                    await File.WriteAllBytesAsync(localPath, bytes);

                    if (!string.Equals(manifestDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(localPath, targetPath, overwrite: true);
                    }

                    AddFileToSnapshot(snapshotFiles, hashInput, localPath, localName, file.Description);
                }
                else
                {
                    if (File.Exists(localPath))
                    {
                        AddFileToSnapshot(snapshotFiles, hashInput, localPath, localName, file.Description);
                    }
                    else
                    {
                        snapshotFiles.Add(new SnapshotFile
                        {
                            FileName = localName,
                            ContentFile = localName,
                            ContentType = GetContentType(localName),
                            Description = file.Description
                        });
                    }
                }
            }
            else
            {
                var localPath = Path.Combine(manifestDirectory, localName);
                if (File.Exists(localPath))
                {
                    AddFileToSnapshot(snapshotFiles, hashInput, localPath, localName, file.Description);
                }
            }
        }

        var sourceHash = "sha256:" + ComputeStringHash(hashInput.ToString());
        var liveSources = !dryRun && loadLiveSource != null
            ? await LoadLiveSources(LiveSource.Urls(manifest), loadLiveSource)
            : null;

        var snapshot = new Snapshot
        {
            Created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Tool = $"pecmd LexSnapshot {ToolVersion}",
            Authority = manifest.Authority,
            Regulation = manifest.Regulation,
            ValidFrom = manifest.ValidFrom,
            Provider = manifest.Provider,
            SourceHash = sourceHash,
            Sources = manifest.Sources,
            Values = resolvedValues,
            Files = snapshotFiles,
            LiveSources = liveSources
        };

        if (!dryRun)
        {
            var snapshotPath = Path.Combine(targetDirectory, "snapshot.json");
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(snapshotPath, json, Encoding.UTF8);
        }

        return snapshot;
    }

    /// <summary>
    /// Download from the first URL that answers; the error of the last URL when none answers
    /// </summary>
    private static async Task<byte[]> DownloadAsync(List<string> urls)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                return await HttpClient.GetByteArrayAsync(urls[i]);
            }
            catch (HttpRequestException) when (i < urls.Count - 1)
            {
                // next URL
            }
        }
    }

    private static async Task<List<LiveSource>> LoadLiveSources(List<string> urls,
        Func<string, Task<SourceDocument>> loadLiveSource)
    {
        using var gate = new SemaphoreSlim(MaxParallelLiveSources);
        var tasks = new List<Task<LiveSource>>(urls.Count);
        foreach (var url in urls)
        {
            tasks.Add(LoadLiveSource(url, loadLiveSource, gate));
        }
        return [.. await Task.WhenAll(tasks)];
    }

    private static async Task<LiveSource> LoadLiveSource(string url,
        Func<string, Task<SourceDocument>> loadLiveSource, SemaphoreSlim gate)
    {
        await gate.WaitAsync();
        try
        {
            return LiveSource.From(url, await loadLiveSource(url));
        }
        catch (Exception exception)
        {
            return new LiveSource { Url = url, Reason = exception.Message };
        }
        finally
        {
            gate.Release();
        }
    }

    private static void AddFileToSnapshot(
        List<SnapshotFile> snapshotFiles,
        StringBuilder hashInput,
        string filePath,
        string localName,
        string description)
    {
        var fileHash = ComputeFileHash(filePath);
        hashInput.Append(fileHash);

        var fileInfo = new FileInfo(filePath);
        snapshotFiles.Add(new SnapshotFile
        {
            FileName = localName,
            ContentFile = localName,
            ContentType = GetContentType(localName),
            Sha256 = fileHash,
            FileSizeBytes = fileInfo.Length,
            Description = description
        });
    }

    private static string ComputeFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static string ComputeStringHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static string ResolvePath(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }
        return Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
    }

    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".xml" => "application/xml",
            // ReSharper disable StringLiteralTypo
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            // ReSharper restore StringLiteralTypo
            ".xls" => "application/vnd.ms-excel",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".zip" => "application/zip",
            ".html" or ".htm" => "text/html",
            _ => "application/octet-stream"
        };
    }
}
