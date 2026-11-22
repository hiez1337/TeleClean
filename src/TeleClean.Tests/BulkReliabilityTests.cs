using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TeleClean.Tests.Mocks;
using TL;
using Xunit;

namespace TeleClean.Tests;

public sealed class BulkReliabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopInterruptsLongPacingAndFloodWait(bool floodWait)
    {
        var telegram = new FakeTelegramService { DefaultLeaveResult = floodWait ? LeaveResult.RateLimited : LeaveResult.Success };
        var engine = new BulkExecutionEngine(telegram, new FakeCacheService());
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.ProgressChanged += (_, report) => { if (report.CurrentIndex == 1) waiting.TrySetResult(); };
        var operation = engine.StartAsync(new[] { new DialogItem { Id = 1 }, new DialogItem { Id = 2 } },
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Let the loop enter its delay, rather than stopping inside its progress callback.
        await Task.Delay(100);
        engine.Stop();
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(BulkOperationState.Interrupted, engine.State);
        Assert.Single(telegram.LeaveCalls);
    }

    [Fact]
    public async Task BatchDeduplicatesTypedIdentityAndSkipsAlreadyProcessedDialogs()
    {
        var telegram = new FakeTelegramService();
        var engine = new BulkExecutionEngine(telegram, new FakeCacheService());
        await engine.StartAsync(new[]
        {
            new DialogItem { Id = 1 }, new DialogItem { Id = 1 },
            new DialogItem { Id = 1, DialogType = DialogType.Bot },
            new DialogItem { Id = 2, IsJoined = false }
        }, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(new[] { (1L, DialogType.Channel), (1L, DialogType.Bot) }, telegram.LeaveCalls);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public async Task NegativeDelaysFailBeforeAnyRequest(int delay, int backoff)
    {
        var telegram = new FakeTelegramService();
        var engine = new BulkExecutionEngine(telegram, new FakeCacheService());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.StartAsync(new[] { new DialogItem { Id = 1 } },
            TimeSpan.FromSeconds(delay), TimeSpan.FromSeconds(backoff)));
        Assert.Empty(telegram.LeaveCalls);
        Assert.Equal(BulkOperationState.Idle, engine.State);
    }

    [Fact]
    public async Task HistoryDeletionContinuesUntilTelegramReportsNoRemainingBatches()
    {
        var offsets = new Queue<int>(new[] { 20, 10, 0 });
        await TelegramService.CompleteHistoryDeletionAsync(() => Task.FromResult(new Messages_AffectedHistory { offset = offsets.Dequeue() }), default);
        Assert.Empty(offsets);
    }

    [Fact]
    public async Task HistoryDeletionPropagatesFailureInsteadOfReportingPartialSuccess()
    {
        int calls = 0;
        await Assert.ThrowsAsync<IOException>(() => TelegramService.CompleteHistoryDeletionAsync(() =>
            ++calls == 1 ? Task.FromResult(new Messages_AffectedHistory { offset = 1 }) : throw new IOException("Disconnected"), default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task HistoryDeletionObservesCancellationBetweenBatches()
    {
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TelegramService.CompleteHistoryDeletionAsync(() =>
        {
            calls++;
            cancellation.Cancel();
            return Task.FromResult(new Messages_AffectedHistory { offset = 1 });
        }, cancellation.Token));
        Assert.Equal(1, calls);
    }
}
