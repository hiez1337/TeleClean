using TeleClean.Core.Models;

namespace TeleClean.Core.Interfaces;

/// <summary>
/// Service abstraction for local persistence of dialog metadata, avatars, and history logs.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Loads dialog metadata from local disk cache (channel_cache.json).
    /// </summary>
    Task<IReadOnlyList<DialogItem>> LoadDialogCacheAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists dialog metadata to local disk cache (channel_cache.json).
    /// </summary>
    Task SaveDialogCacheAsync(IEnumerable<DialogItem> dialogs, CancellationToken ct = default);

    /// <summary>
    /// Loads bulk execution history from the legacy JSON history and the append-only JSONL journal.
    /// </summary>
    Task<IReadOnlyList<BulkLogEntry>> LoadHistoryAsync(CancellationToken ct = default);

    /// <summary>
    /// Appends a new log entry to the append-only leave_history.jsonl journal.
    /// </summary>
    Task AppendHistoryAsync(BulkLogEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Returns the absolute path where an avatar for the given dialog ID should be cached.
    /// </summary>
    string GetAvatarPath(long dialogId);

    /// <summary>
    /// Checks if a valid, non-corrupt avatar exists in disk cache for the given dialog ID.
    /// </summary>
    bool HasAvatar(long dialogId);

    /// <summary>
    /// Saves avatar binary stream to disk cache with integrity verification.
    /// </summary>
    Task<string> SaveAvatarAsync(long dialogId, Stream stream, CancellationToken ct = default);

    Task ClearAvatarCacheAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Clears the dialog snapshot.
    /// </summary>
    Task ClearCacheAsync(CancellationToken ct = default);
}
