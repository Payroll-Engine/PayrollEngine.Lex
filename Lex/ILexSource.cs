using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Read access to lex manifests, independent of where they are stored. <see cref="WorkspaceLexSource"/> reads
/// the lex.json files of a folder tree; other implementations may read packaged snapshots.
/// </summary>
public interface ILexSource
{
    /// <summary>
    /// Summaries of all manifests of the source; a manifest that cannot be read is listed with its error
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<IReadOnlyList<LexManifestInfo>> ListManifestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Summary of one manifest
    /// </summary>
    /// <param name="manifestId">Manifest identifier (<see cref="LexManifestInfo.Id"/>)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The summary, null for an unknown manifest</returns>
    Task<LexManifestInfo> GetManifestAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// All entries of a manifest in document order
    /// </summary>
    /// <param name="manifestId">Manifest identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The entries, null for an unknown or unreadable manifest</returns>
    Task<IReadOnlyList<LexEntryInfo>> GetEntriesAsync(string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One entry of a manifest
    /// </summary>
    /// <param name="manifestId">Manifest identifier</param>
    /// <param name="key">Entry key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The entry, null for an unknown manifest or key</returns>
    Task<LexEntryInfo> GetEntryAsync(string manifestId, string key, CancellationToken cancellationToken = default);
}
