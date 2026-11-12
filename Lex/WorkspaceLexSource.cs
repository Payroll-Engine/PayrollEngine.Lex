using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Reads the lex.json files of a folder tree. The manifest identifier is the full path of lex.json.
/// Nothing is cached: every call reads the files, so changes made by other tools are seen at once.
/// </summary>
public sealed class WorkspaceLexSource : ILexSource
{
    private const int MaxDifferences = 20;

    private readonly Func<IReadOnlyList<string>> resolve;

    /// <summary>
    /// A source over a fixed list of manifest files
    /// </summary>
    /// <param name="lexPaths">Paths of lex.json files</param>
    public WorkspaceLexSource(IEnumerable<string> lexPaths)
    {
        ArgumentNullException.ThrowIfNull(lexPaths);
        var paths = lexPaths.Select(Path.GetFullPath).ToList();
        resolve = () => paths;
    }

    private WorkspaceLexSource(Func<IReadOnlyList<string>> resolve)
    {
        this.resolve = resolve;
    }

    /// <summary>
    /// A source over the lex.json files below a root folder; the files are searched on every call
    /// (<see cref="FileResolver.Resolve(string, string, string, bool)"/>)
    /// </summary>
    /// <param name="root">Root folder that contains the repository folders</param>
    /// <param name="repoMask">Mask of the repository folder names, default all</param>
    /// <param name="fileMask">File name or mask relative to a repository folder, default lex.json</param>
    /// <param name="recursive">Search all subfolders of a repository folder</param>
    public static WorkspaceLexSource FromRoot(string root, string repoMask = null, string fileMask = null, bool recursive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new WorkspaceLexSource(() => FileResolver.Resolve(root, repoMask, fileMask, recursive));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LexManifestInfo>> ListManifestsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<LexManifestInfo>();
        foreach (var path in resolve())
        {
            cancellationToken.ThrowIfCancellationRequested();
            list.Add(Load(path).Info());
        }
        return Task.FromResult<IReadOnlyList<LexManifestInfo>>(list);
    }

    /// <inheritdoc />
    public Task<LexManifestInfo> GetManifestAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        var path = Find(manifestId);
        return Task.FromResult(path == null ? null : Load(path).Info());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LexEntryInfo>> GetEntriesAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        var path = Find(manifestId);
        var loaded = path == null ? null : Load(path);
        if (loaded?.Manifest == null)
        {
            return Task.FromResult<IReadOnlyList<LexEntryInfo>>(null);
        }
        return Task.FromResult<IReadOnlyList<LexEntryInfo>>(
            loaded.Manifest.Values.Select(loaded.EntryInfo).ToList());
    }

    /// <inheritdoc />
    public async Task<LexEntryInfo> GetEntryAsync(string manifestId, string key, CancellationToken cancellationToken = default)
    {
        var entries = await GetEntriesAsync(manifestId, cancellationToken);
        return entries?.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal));
    }

    /// <summary>The manifest file of an identifier, null when the source does not hold it</summary>
    private string Find(string manifestId)
    {
        if (string.IsNullOrWhiteSpace(manifestId))
        {
            return null;
        }
        string full;
        try
        {
            full = Path.GetFullPath(manifestId);
        }
        catch (ArgumentException)
        {
            return null;
        }
        return resolve().FirstOrDefault(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase));
    }

    private static Loaded Load(string lexPath)
    {
        var loaded = new Loaded { LexPath = lexPath };
        try
        {
            var bytes = File.ReadAllBytes(lexPath);
            loaded.Hash = ManifestHash.OfBytes(bytes);
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            loaded.Manifest = JsonSerializer.Deserialize<Manifest>(Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset),
                LexJson.ManifestOptions) ?? new Manifest();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            loaded.Error = exception.Message;
            return loaded;
        }

        var lexDirectory = Path.GetDirectoryName(lexPath)!;
        var snapshotPath = Path.Combine(lexDirectory, SnapshotOperation.SnapshotFileName);
        if (!File.Exists(snapshotPath))
        {
            loaded.SnapshotState = LexSnapshotState.Missing;
            return loaded;
        }
        try
        {
            loaded.Snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(snapshotPath), LexJson.SnapshotOptions);
            if (loaded.Snapshot == null)
            {
                throw new JsonException("snapshot.json is empty");
            }
            loaded.Differences = Verifier.Verify(loaded.Manifest, loaded.Snapshot, lexDirectory);
            loaded.SnapshotState = loaded.Differences.Count == 0 ? LexSnapshotState.Current : LexSnapshotState.Stale;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            loaded.Snapshot = null;
            loaded.SnapshotState = LexSnapshotState.Unreadable;
            loaded.Differences = [exception.Message];
        }
        return loaded;
    }

    private sealed class Loaded
    {
        public string LexPath { get; init; }
        public string Hash { get; set; }
        public string Error { get; set; }
        public Manifest Manifest { get; set; }
        public Snapshot Snapshot { get; set; }
        public LexSnapshotState SnapshotState { get; set; }
        public List<string> Differences { get; set; } = [];

        public LexManifestInfo Info()
        {
            if (Manifest == null)
            {
                return new LexManifestInfo
                {
                    Id = LexPath,
                    Satellite = SatelliteName(),
                    Error = Error,
                    ContentHash = Hash,
                    SnapshotState = LexSnapshotState.Missing
                };
            }
            var values = Manifest.Values;
            return new LexManifestInfo
            {
                Id = LexPath,
                Satellite = SatelliteName(),
                Authority = Manifest.Authority,
                Regulation = Manifest.Regulation,
                ValidFrom = Manifest.ValidFrom,
                Provider = Manifest.Provider,
                Languages = Manifest.Languages?.ToList() ?? [],
                Entries = values.Count,
                OpenEntries = values.Count(x => x.Open),
                AutoVerified = values.Count(x => x.Verification?.Mode == Verification.ModeAuto),
                ManualVerified = values.Count(x => x.Verification?.Mode == Verification.ModeManual),
                WithoutVerification = values.Count(x => x.Verification == null),
                Sources = Manifest.Sources.Count,
                Files = Manifest.Files.Count,
                Exclusions = Manifest.Exclusions.Count,
                SnapshotState = SnapshotState,
                SnapshotDifferences = Differences.Take(MaxDifferences).ToList(),
                ContentHash = Hash
            };
        }

        public LexEntryInfo EntryInfo(Entry entry)
        {
            var urls = entry.Urls.ToHashSet(StringComparer.Ordinal);
            var sources = Manifest.Sources.Where(x => x.Url != null && urls.Contains(x.Url)).ToList();
            var keys = DerivedProperties.Keys(Manifest);
            var exclusions = Manifest.Exclusions
                .Where(x => DerivedProperties.IsPropertyKind(x.Kind)
                    ? DerivedProperties.Resolve(x.Pattern, keys) is var (key, _) &&
                      string.Equals(key, entry.Key, StringComparison.Ordinal)
                    : x.Matches(entry.Key))
                .ToList();
            return new LexEntryInfo
            {
                ManifestId = LexPath,
                Entry = entry,
                Sources = sources,
                Review = sources.Any(x => x.IsFourEyes)
                    ? Source.ReviewFourEyes
                    : sources.Select(x => x.Review).FirstOrDefault(x => x != null),
                Exclusions = exclusions,
                SnapshotState = EntryState(entry),
                ContentHash = Hash
            };
        }

        /// <summary>The fields of the snapshot hash, the same rules as <see cref="Verifier"/></summary>
        private LexEntrySnapshotState EntryState(Entry entry)
        {
            if (Snapshot == null)
            {
                return LexEntrySnapshotState.NoSnapshot;
            }
            var snapshot = Snapshot.Values?.FirstOrDefault(x => string.Equals(x.Key, entry.Key, StringComparison.Ordinal));
            if (snapshot == null)
            {
                return LexEntrySnapshotState.NotInSnapshot;
            }
            var contentMap = entry.HasContentMap ? Path.GetFileName(entry.ContentMap) : null;
            var same =
                (!entry.HasInlineContent || string.Equals(entry.Content, snapshot.Content, StringComparison.Ordinal)) &&
                string.Equals(contentMap, snapshot.ContentMap, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.SourceQuote, snapshot.SourceQuote, StringComparison.Ordinal) &&
                entry.ReferenceYear == snapshot.ReferenceYear &&
                entry.Open == snapshot.Open &&
                (entry.Verification?.SameAs(snapshot.Verification) ?? snapshot.Verification == null);
            return same ? LexEntrySnapshotState.Current : LexEntrySnapshotState.Changed;
        }

        private string SatelliteName()
        {
            var lexDirectory = Path.GetDirectoryName(LexPath)!;
            return Path.GetFileName(Directory.GetParent(lexDirectory)?.FullName ?? lexDirectory);
        }
    }
}
