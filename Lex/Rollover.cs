using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PayrollEngine.Lex;

/// <summary>
/// Line of a rolled file that still mentions the previous year
/// </summary>
public sealed class YearMention
{
    /// <summary>File path relative to the new satellite</summary>
    public string File { get; init; }

    /// <summary>Line number (1-based)</summary>
    public int Line { get; init; }

    /// <summary>Line text (trimmed, shortened)</summary>
    public string Text { get; init; }
}

/// <summary>
/// Result of a rollover of one data regulation satellite
/// </summary>
public sealed class RolloverResult
{
    /// <summary>Directory of the previous cycle</summary>
    public string SourceDirectory { get; init; }

    /// <summary>Directory of the new cycle</summary>
    public string TargetDirectory { get; internal set; }

    /// <summary>validFrom of the previous cycle (lex.json)</summary>
    public string PreviousValidFrom { get; internal set; }

    /// <summary>validFrom of the new cycle (cycle start)</summary>
    public string ValidFrom { get; init; }

    /// <summary>Files of the new satellite, relative paths</summary>
    public List<string> Files { get; } = [];

    /// <summary>Renamed files and folders (previous name -> new name)</summary>
    public List<string> Renamed { get; } = [];

    /// <summary>Changed files with the kind of change</summary>
    public List<string> Changed { get; } = [];

    /// <summary>Lex entries marked as open</summary>
    public int OpenEntries { get; internal set; }

    /// <summary>Lines that still mention the previous year: review them</summary>
    public List<YearMention> YearMentions { get; } = [];

    /// <summary>Information for the follow-up work</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Error that prevented the rollover, nothing written</summary>
    public string Error { get; internal set; }
}

/// <summary>
/// Prepares the satellite of the next cycle: the folder is copied with the year tag of the cycle start,
/// validFrom and the year-based version are set and all lex entries are marked as open.
/// Values, descriptions and lookup keys are not changed.
/// </summary>
public static class Rollover
{
    private const string LexFolder = ".lex";
    private const string LexFileName = "lex.json";
    private const string SnapshotFileName = "snapshot.json";
    private const int MaxMentionLength = 160;

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "nupkgs", ".vs"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".pecmd", ".props", ".targets", ".csproj", ".config", ".md", ".txt", ".cmd", ".sh",
        ".xml", ".yml", ".yaml", ".cs"
    };

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Roll a satellite to the cycle starting at the given date
    /// </summary>
    /// <param name="satelliteDirectory">Satellite directory of the previous cycle (contains .lex/lex.json)</param>
    /// <param name="cycleStart">Start of the new cycle, the new validFrom</param>
    /// <param name="dryRun">Report without writing</param>
    public static RolloverResult Run(string satelliteDirectory, DateTime cycleStart, bool dryRun)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(satelliteDirectory));
        var result = new RolloverResult
        {
            SourceDirectory = source,
            ValidFrom = cycleStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        var lexDirectory = Path.Combine(source, LexFolder);
        var lexPath = Path.Combine(lexDirectory, LexFileName);
        if (!File.Exists(lexPath))
        {
            result.Error = $"No {LexFolder}/{LexFileName} in {source}";
            return result;
        }
        Manifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(lexPath), ReadOptions);
        }
        catch (JsonException exception)
        {
            result.Error = "lex.json could not be read: " + exception.Message;
            return result;
        }
        if (manifest == null)
        {
            result.Error = "lex.json is empty";
            return result;
        }
        result.PreviousValidFrom = manifest.ValidFrom;
        if (!EntryRules.IsDate(manifest.ValidFrom))
        {
            result.Error = $"lex.json has no valid validFrom (yyyy-MM-dd): '{manifest.ValidFrom}'";
            return result;
        }
        var previous = DateTime.ParseExact(manifest.ValidFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (cycleStart.Year <= previous.Year)
        {
            result.Error = $"The cycle start {result.ValidFrom} does not follow the year of validFrom {manifest.ValidFrom}";
            return result;
        }
        var oldTag = previous.Year.ToString(CultureInfo.InvariantCulture);
        var newTag = cycleStart.Year.ToString(CultureInfo.InvariantCulture);

        var sourceName = Path.GetFileName(source);
        var edition = EditionSuffix(sourceName, oldTag);
        if (edition == null)
        {
            result.Error = $"The folder name {sourceName} has no year tag .{oldTag}";
            return result;
        }
        string RenameName(string name) => RenameSegment(name, oldTag, newTag, edition);
        var targetName = RenameName(sourceName);
        var target = Path.Combine(Path.GetDirectoryName(source)!, targetName);
        result.TargetDirectory = target;
        if (Directory.Exists(target) || File.Exists(target))
        {
            result.Error = $"The target {target} exists";
            return result;
        }

        // content files of the entries are renamed, the other files of .lex keep their name (evidence)
        var contentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Values.Where(x => x.HasContentFile))
        {
            contentFiles.Add(Path.GetFullPath(Path.Combine(lexDirectory, entry.ContentFile)));
        }

        // file plan
        var names = new Dictionary<string, string>(StringComparer.Ordinal) { [sourceName] = targetName };
        result.Renamed.Add($"{sourceName} -> {targetName}");
        var plan = new List<(string Source, string Relative)>();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(source, file);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            if (parts.Take(parts.Length - 1).Any(ExcludedDirectories.Contains))
            {
                continue;
            }
            var inLex = parts.Length > 1 && string.Equals(parts[0], LexFolder, StringComparison.OrdinalIgnoreCase);
            if (inLex && parts.Length == 2 && string.Equals(parts[1], SnapshotFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            for (var i = 0; i < parts.Length; i++)
            {
                var isFile = i == parts.Length - 1;
                if (inLex && (!isFile || !contentFiles.Contains(file)))
                {
                    continue;
                }
                var renamed = RenameName(parts[i]);
                if (!string.Equals(renamed, parts[i], StringComparison.Ordinal))
                {
                    if (names.TryAdd(parts[i], renamed))
                    {
                        result.Renamed.Add($"{parts[i]} -> {renamed}");
                    }
                    parts[i] = renamed;
                }
            }
            plan.Add((file, string.Join(Path.DirectorySeparatorChar, parts)));
        }

        // regulation validFrom tokens by file
        RegulationData regulation;
        try
        {
            regulation = RegulationReader.Read(source);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            result.Error = "Regulation JSON could not be read: " + exception.Message;
            return result;
        }
        var validFromTokens = regulation.ValidFromTokens
            .GroupBy(x => x.File.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        if (validFromTokens.Count == 0)
        {
            result.Notes.Add("No regulation validFrom in the Regulation folder: set validFrom of the regulation by hand");
        }

        var references = names
            .Where(x => !string.Equals(x.Key, x.Value, StringComparison.Ordinal))
            .OrderByDescending(x => x.Key.Length)
            .ToList();
        var mention = new Regex($"(?<![0-9]){oldTag}(?![0-9])", RegexOptions.CultureInvariant);

        // file contents
        var output = new List<(string Relative, string Source, byte[] Content)>();
        foreach (var (file, relative) in plan)
        {
            result.Files.Add(relative);
            var isLex = string.Equals(file, lexPath, StringComparison.OrdinalIgnoreCase);
            if (isLex)
            {
                var editor = ManifestEditor.Parse(File.ReadAllText(file));
                editor.SetValidFrom(result.ValidFrom);
                var keys = editor.RollContentFiles(x => RenamePath(x, RenameName), oldTag, newTag);
                result.OpenEntries = editor.OpenEntries();
                var json = editor.ToJson();
                result.Changed.Add($"{relative}: validFrom {result.ValidFrom}, {result.OpenEntries} open entries" +
                                   (keys > 0 ? $", {keys} keys with year tag {newTag}" : string.Empty));
                AddMentions(result, relative, json, mention);
                output.Add((relative, null, Encoding.UTF8.GetBytes(json)));
                continue;
            }
            var inLex = relative.StartsWith(LexFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (inLex || !TextExtensions.Contains(Path.GetExtension(file)))
            {
                output.Add((relative, file, null));
                continue;
            }

            var bytes = File.ReadAllBytes(file);
            var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var content = bom ? bytes[3..] : bytes;
            var changes = new List<string>();
            if (validFromTokens.TryGetValue(file, out var tokens))
            {
                content = SetValidFrom(content, tokens, result.ValidFrom, relative, result.Notes, out var count);
                if (count > 0)
                {
                    changes.Add("validFrom");
                }
            }
            string text;
            try
            {
                text = StrictUtf8.GetString(content);
            }
            catch (DecoderFallbackException)
            {
                output.Add((relative, file, null));
                continue;
            }
            var edited = SetVersion(Path.GetFileName(file), text, oldTag, newTag);
            if (!string.Equals(edited, text, StringComparison.Ordinal))
            {
                changes.Add("version");
                text = edited;
            }
            edited = references.Aggregate(text, (current, name) => current.Replace(name.Key, name.Value, StringComparison.Ordinal));
            if (!string.Equals(edited, text, StringComparison.Ordinal))
            {
                changes.Add("file references");
                text = edited;
            }
            if (changes.Count > 0)
            {
                result.Changed.Add($"{relative}: {string.Join(", ", changes.Distinct())}");
            }
            AddMentions(result, relative, text, mention);
            var encoded = StrictUtf8.GetBytes(text);
            output.Add((relative, null, bom ? [0xEF, 0xBB, 0xBF, .. encoded] : encoded));
        }

        result.Notes.Add($"Not part of the rollover: solution and test projects, references outside {targetName}, removing {sourceName}");
        result.Notes.Add("Next steps: enter and verify the values of the open entries, LexSnapshot, LexApply, LexCheck");
        if (dryRun)
        {
            return result;
        }

        try
        {
            foreach (var (relative, file, content) in output)
            {
                var path = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (content != null)
                {
                    File.WriteAllBytes(path, content);
                }
                else
                {
                    File.Copy(file, path);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result.Error = "The new satellite could not be written: " + exception.Message;
            TryDelete(target);
        }
        return result;
    }

    /// <summary>
    /// Edition suffix of the year tag in a folder name: empty for Data.Tax.2026, "b" for the mid-year
    /// edition Data.Tax.2026b, null without year tag
    /// </summary>
    internal static string EditionSuffix(string folderName, string year)
    {
        var match = Regex.Match(folderName, $@"(?<=(^|\.)){year}(?<suffix>[a-z]?)(?=(\.|$))", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["suffix"].Value : null;
    }

    /// <summary>
    /// Replace the year tag of a dot-separated name segment (e.g. Data.Tax.2026.json); the edition suffix
    /// of the folder (e.g. 2026b) is dropped in the new cycle
    /// </summary>
    internal static string RenameSegment(string name, string oldTag, string newTag, string edition = "") =>
        Regex.Replace(name, $@"(?<=(^|\.)){oldTag}{(edition.Length > 0 ? $"(?:{Regex.Escape(edition)})?" : string.Empty)}(?=(\.|$))",
            newTag, RegexOptions.CultureInvariant);

    private static string RenamePath(string path, Func<string, string> renameName) =>
        string.Join('/', path.Replace('\\', '/').Split('/').Select(renameName));

    /// <summary>
    /// Replace the date part of the validFrom tokens, the time part is kept
    /// </summary>
    private static byte[] SetValidFrom(byte[] content, List<RegulationToken> tokens, string validFrom,
        string relative, List<string> notes, out int count)
    {
        count = 0;
        var output = new List<byte>(content.Length);
        var position = 0;
        foreach (var token in tokens.OrderBy(x => x.Offset))
        {
            if (token.Value == null || token.Value.Length < 10 || !EntryRules.IsDate(token.Value[..10]))
            {
                notes.Add($"{relative}: validFrom '{token.Value}' is no date, not changed");
                continue;
            }
            output.AddRange(content[position..token.Offset]);
            output.AddRange(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(validFrom + token.Value[10..], WriteOptions)));
            position = token.Offset + token.Length;
            count++;
        }
        output.AddRange(content[position..]);
        return output.ToArray();
    }

    private static string SetVersion(string fileName, string text, string oldTag, string newTag)
    {
        if (string.Equals(fileName, "Directory.Build.props", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.Replace(text, $@"(<(?:Version|FileVersion)>\s*){oldTag}(?=[.\-<])", "${1}" + newTag,
                RegexOptions.CultureInvariant);
        }
        if (string.Equals(fileName, "regulation-package.json", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.Replace(text, $@"(""version""\s*:\s*""){oldTag}(?=[.\-""])", "${1}" + newTag,
                RegexOptions.CultureInvariant);
        }
        return text;
    }

    private static void AddMentions(RolloverResult result, string relative, string text, Regex mention)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!mention.IsMatch(lines[i]))
            {
                continue;
            }
            var line = lines[i].Trim();
            result.YearMentions.Add(new YearMention
            {
                File = relative,
                Line = i + 1,
                Text = line.Length > MaxMentionLength ? line[..MaxMentionLength] + "..." : line
            });
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the error of the rollover is reported, the partial folder stays
        }
    }
}
