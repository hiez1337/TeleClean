using TeleClean.Core.Interfaces;
using TeleClean.Core.Models;

namespace TeleClean.Tests.Mocks;

/// <summary>
/// In-memory fake implementation of ICacheService for engine and coordinator unit tests.
/// </summary>
public sealed class FakeCacheService : ICacheService
{
    private readonly List<DialogItem> _cachedDialogs = new();
    private readonly List<BulkLogEntry> _history = new();
    private readonly HashSet<long> _savedAvatars = new();

    public IReadOnlyList<DialogItem> CachedDialogs => _cachedDialogs;
    public IReadOnlyList<BulkLogEntry> History => _history;

    public Task<IReadOnlyList<DialogItem>> LoadDialogCacheAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<DialogItem>>(_cachedDialogs.Select(d => d.Clone()).ToList());
    }

    public Task SaveDialogCacheAsync(IEnumerable<DialogItem> dialogs, CancellationToken ct = default)
    {
        _cachedDialogs.Clear();
        _cachedDialogs.AddRange(dialogs.Select(d => d.Clone()));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BulkLogEntry>> LoadHistoryAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<BulkLogEntry>>(_history.ToList());
    }

    public Task AppendHistoryAsync(BulkLogEntry entry, CancellationToken ct = default)
    {
        _history.Add(entry);
        return Task.CompletedTask;
    }

    public string GetAvatarPath(long dialogId)
    {
        return $"/fake/avatars/{dialogId}.jpg";
    }

    public bool HasAvatar(long dialogId)
    {
        return _savedAvatars.Contains(dialogId);
    }

    public async Task<string> SaveAvatarAsync(long dialogId, Stream stream, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        _savedAvatars.Add(dialogId);
        return GetAvatarPath(dialogId);
    }

    public Task ClearCacheAsync(CancellationToken ct = default)
    {
        _cachedDialogs.Clear();
        return Task.CompletedTask;
    }
}
