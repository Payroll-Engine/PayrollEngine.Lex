using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Generates snapshot.json from a validated <see cref="LexManifest"/> and copies referenced files.
/// Downloads files from sourceUrl when no local sourcePath is provided.
/// </summary>
public sealed class LexSnapshotWriter
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

    /// <summary>
    /// Creates the snapshot: downloads/copies files and writes snapshot.json
    /// </summary>
    /// <param name="manifest">Validated manifest</param>
    /// <param name="manifestDirectory">Directory of lex.json (source root)</param>
    /// <param name="targetDirectory">Output directory (default: same as manifestDirectory)</param>
    /// <param name="dryRun">When true, reports without writing anything</param>
    /// <param name="writeFile">Optional callback with the file name and the dry-run flag, invoked for every
    /// file downloaded from its source URL, in dry-run mode as well</param>
    /// <returns>Written snapshot (for reporting)</returns>
    public async Task<LexSnapshot> WriteAsync(
        LexManifest manifest,
        string manifestDirectory,
        string targetDirectory,
        bool dryRun,
        Action<string, bool> writeFile = null)
    {
        var snapshotFiles = new List<LexSnapshotFile>();
        var hashInput = new StringBuilder();

        // process values with contentFile
        var resolvedValues = new List<LexValue>();
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
                snapshotFiles.Add(new LexSnapshotFile
                {
                    FileName = localName,
                    ContentFile = localName,
                    ContentType = GetContentType(localName),
                    Sha256 = fileHash,
                    FileSizeBytes = fileInfo.Length,
                    Description = value.Description
                });

                resolvedValues.Add(new LexValue
                {
                    Key = value.Key,
                    ContentFile = localName,
                    DocumentRef = value.DocumentRef,
                    Unit = value.Unit,
                    SourceUrl = value.SourceUrl,
                    SourceLocation = value.SourceLocation,
                    LegalReference = value.LegalReference,
                    Description = value.Description
                });
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
            else if (!string.IsNullOrWhiteSpace(file.SourceUrl))
            {
                var localPath = Path.Combine(manifestDirectory, localName);

                writeFile?.Invoke(localName, dryRun);
                if (!dryRun)
                {
                    var bytes = await HttpClient.GetByteArrayAsync(file.SourceUrl);
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
                        snapshotFiles.Add(new LexSnapshotFile
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

        var snapshot = new LexSnapshot
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
            Files = snapshotFiles
        };

        if (!dryRun)
        {
            var snapshotPath = Path.Combine(targetDirectory, "snapshot.json");
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(snapshotPath, json, Encoding.UTF8);
        }

        return snapshot;
    }

    private static void AddFileToSnapshot(
        List<LexSnapshotFile> snapshotFiles,
        StringBuilder hashInput,
        string filePath,
        string localName,
        string description)
    {
        var fileHash = ComputeFileHash(filePath);
        hashInput.Append(fileHash);

        var fileInfo = new FileInfo(filePath);
        snapshotFiles.Add(new LexSnapshotFile
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
