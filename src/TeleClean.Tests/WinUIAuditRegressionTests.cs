using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TeleClean.Tests.Mocks;
using Xunit;

namespace TeleClean.Tests;

public sealed class WinUIAuditRegressionTests
{
    [Fact]
    public void AvatarKeysSeparateTheThreeTelegramPeerNamespaces()
    {
        const long id = 3900000000L;
        long Key(DialogType kind) => new DialogItem { Id = id, DialogType = kind }.AvatarCacheKey;

        Assert.Equal(Key(DialogType.Channel), Key(DialogType.Supergroup));
        Assert.Equal(Key(DialogType.Bot), Key(DialogType.Deleted));
        Assert.Equal(3, new[] { Key(DialogType.Channel), Key(DialogType.Group), Key(DialogType.Bot) }.Distinct().Count());
        Assert.All(new[] { Key(DialogType.Channel), Key(DialogType.Group), Key(DialogType.Bot) },
            key => Assert.True(key < 0));
    }

    [Fact]
    public async Task StoppingInFlightDoesNotPermitSecondBatchAndReportsCommittedResult()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<LeaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeTelegramService
        {
            LeaveHandler = (_, _) =>
            {
                started.SetResult();
                return response.Task;
            }
        };
        var cache = new FakeCacheService();
        var engine = new BulkExecutionEngine(service, cache);
        BulkProgressReport? last = null;
        engine.ProgressChanged += (_, report) => last = report;
        var items = new[]
        {
            new DialogItem { Id = 42, DialogType = DialogType.Bot },
            new DialogItem { Id = 42, DialogType = DialogType.Channel }
        };
        var batch = engine.StartAsync(items, TimeSpan.Zero, TimeSpan.Zero);
        await started.Task;
        engine.Stop();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.StartAsync(items, TimeSpan.Zero, TimeSpan.Zero));
        }
        finally { response.SetResult(LeaveResult.Success); }
        await batch;

        Assert.Equal(BulkOperationState.Interrupted, last!.State);
        Assert.Equal(1, last.CurrentIndex);
        Assert.Equal(1, last.SuccessCount);
        Assert.Equal(DialogType.Bot, Assert.Single(cache.History).DialogType);
        Assert.Equal(DialogType.Bot, Assert.Single(service.LeaveCalls).DialogType);
        Assert.True(items[1].IsJoined);
    }

    [Fact]
    public async Task WaitUsesServerDurationWhenItExceedsLocalBackoff()
    {
        var service = new FakeTelegramService
        {
            DefaultLeaveResult = LeaveResult.RateLimited,
            RateLimitRetryAfter = TimeSpan.FromSeconds(90)
        };
        var engine = new BulkExecutionEngine(service, new FakeCacheService());
        using var cancellation = new CancellationTokenSource();
        int? waitSeconds = null;
        engine.ProgressChanged += (_, report) =>
        {
            if (report.State != BulkOperationState.RateLimited) return;
            waitSeconds = report.SecondsRemaining;
            cancellation.Cancel();
        };

        await engine.StartAsync(new[] { new DialogItem { Id = 1 } }, TimeSpan.Zero,
            TimeSpan.FromSeconds(1), cancellation.Token);

        Assert.Equal(90, waitSeconds);
        Assert.Equal(BulkOperationState.Interrupted, engine.State);
    }
}
