using TeleClean.Core.Models;
using TeleClean.Core.Services;
using Xunit;

namespace TeleClean.Tests;

public sealed class StorageReliabilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TeleClean-storage-" + Guid.NewGuid().ToString("N"));
    private CacheService Cache => new(new ConfigService(_directory));

    [Fact]
    public async Task AppendPreservesLegacyPythonHistoryWithoutRewritingIt()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "leave_history.json");
        const string legacy = "[{\"started_at\":1700000000,\"total\":2,\"success\":1,\"errors\":[{\"channel_id\":42,\"title\":\"Legacy\",\"error\":\"rate_limited\"}]}]";
        await File.WriteAllTextAsync(path, legacy);
        await Cache.AppendHistoryAsync(new BulkLogEntry(DateTime.UtcNow, 43, "New", LeaveResult.Success, "Done"));

        var history = await Cache.LoadHistoryAsync();

        Assert.Equal(legacy, await File.ReadAllTextAsync(path));
        Assert.Equal(3, history.Count);
        Assert.Contains(history, entry => entry.DialogId == 42 && entry.Result == LeaveResult.RateLimited);
        Assert.Contains(history, entry => entry.DialogId == 43);
        Assert.Contains(history, entry => entry.DialogId == 0 && entry.Message.Contains("2"));
    }

    [Fact]
    public async Task MalformedLegacyRecordDoesNotHideValidRecordsOrNewJournal()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "leave_history.json"),
            "[{\"started_at\":\"invalid\"},{\"dialogId\":42,\"title\":\"Readable\"},null]");
        await Cache.AppendHistoryAsync(new BulkLogEntry(DateTime.UtcNow, 43, "New", LeaveResult.Success, "Done"));

        Assert.Equal(new long[] { 42, 43 }, (await Cache.LoadHistoryAsync()).Select(entry => entry.DialogId));
    }

    [Fact]
    public async Task JournalAppendRecoversAfterPartialRecordAndPreservesCompletePrefix()
    {
        var cache = Cache;
        await cache.AppendHistoryAsync(new BulkLogEntry(DateTime.UtcNow, 1, "First", LeaveResult.Success, "Done"));
        var path = Path.Combine(_directory, "leave_history.jsonl");
        await File.AppendAllTextAsync(path, "{\"dialog_id\":");
        var prefix = await File.ReadAllTextAsync(path);

        await cache.AppendHistoryAsync(new BulkLogEntry(DateTime.UtcNow, 2, "Second", LeaveResult.Success, "Done"));

        Assert.StartsWith(prefix, await File.ReadAllTextAsync(path));
        Assert.Equal(new long[] { 1, 2 }, (await Cache.LoadHistoryAsync()).Select(entry => entry.DialogId));
    }

    [Fact]
    public async Task CancelledCacheReplacementPreservesPreviousFileAndRemovesTemporaryFiles()
    {
        var cache = Cache;
        await cache.SaveDialogCacheAsync(new[] { new DialogItem { Id = 1, Title = "Original" } });
        var path = Path.Combine(_directory, "channel_cache.json");
        var original = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        IEnumerable<DialogItem> CancelDuringSerialization()
        {
            yield return new DialogItem { Id = 2 };
            cancellation.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.SaveDialogCacheAsync(CancelDuringSerialization(), cancellation.Token));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal(1, Assert.Single(await cache.LoadDialogCacheAsync()).Id);
    }

    [Fact]
    public async Task InvalidAvatarReplacementKeepsPreviousImage()
    {
        var cache = Cache;
        // Header validation fixture, deliberately not a renderable photo.
        var image = new byte[32];
        image[0] = 0xff; image[1] = 0xd8; image[2] = 0xff;
        var path = await cache.SaveAvatarAsync(1, new MemoryStream(image));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.SaveAvatarAsync(1, new MemoryStream(new byte[32])));

        Assert.True(cache.HasAvatar(1));
        Assert.Equal(image, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    private sealed class InterruptedImageStream : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync(new byte[32], cancellationToken);
            throw new IOException("Download interrupted after a partial write");
        }
    }

    [Fact]
    public async Task InterruptedAvatarWritePreservesPreviousFileAndCleansTemporaryFile()
    {
        var cache = Cache;
        var image = new byte[32];
        image[0] = 0xff; image[1] = 0xd8; image[2] = 0xff;
        var path = await cache.SaveAvatarAsync(1, new MemoryStream(image));

        await Assert.ThrowsAsync<IOException>(() => cache.SaveAvatarAsync(1, new InterruptedImageStream()));

        Assert.Equal(image, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
