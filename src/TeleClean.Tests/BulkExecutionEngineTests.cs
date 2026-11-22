using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TeleClean.Tests.Mocks;
using Xunit;

namespace TeleClean.Tests;

public class BulkExecutionEngineTests
{
    [Fact]
    public async Task StateTransitions_Idle_Running_Completed()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        Assert.Equal(BulkOperationState.Idle, engine.State);

        var items = new List<DialogItem>
        {
            new DialogItem { Id = -101, Title = "Channel 1", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -102, Title = "Channel 2", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -103, Title = "Channel 3", DialogType = DialogType.Channel, IsJoined = true }
        };

        var observedStates = new List<BulkOperationState>();
        engine.ProgressChanged += (_, report) =>
        {
            observedStates.Add(report.State);
        };

        await engine.StartAsync(items, delayBetween: TimeSpan.Zero, rateLimitDelay: TimeSpan.FromSeconds(1));

        Assert.Equal(BulkOperationState.Completed, engine.State);
        Assert.Contains(BulkOperationState.Running, observedStates);
        Assert.Contains(BulkOperationState.Completed, observedStates);

        Assert.Equal(3, fakeTelegram.LeaveCalls.Count);
        Assert.All(items, item =>
        {
            Assert.False(item.IsJoined);
            Assert.Equal("✅ Вышел", item.StatusBadge);
        });
    }

    [Fact]
    public async Task StateTransitions_EmptyItems_CompletedImmediately()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        await engine.StartAsync(Enumerable.Empty<DialogItem>(), delayBetween: TimeSpan.Zero, rateLimitDelay: TimeSpan.FromSeconds(1));

        Assert.Equal(BulkOperationState.Completed, engine.State);
        Assert.Empty(fakeTelegram.LeaveCalls);
    }

    [Fact]
    public async Task PauseAndResume_ProcessesAllItemsWithoutDropping()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        var items = new List<DialogItem>
        {
            new DialogItem { Id = -201, Title = "Channel A", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -202, Title = "Channel B", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -203, Title = "Channel C", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -204, Title = "Channel D", DialogType = DialogType.Channel, IsJoined = true }
        };

        bool pausedOnce = false;
        var tcsItem2Paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        engine.ProgressChanged += (_, report) =>
        {
            if (report.CurrentIndex == 2 && !pausedOnce)
            {
                pausedOnce = true;
                engine.Pause();
                tcsItem2Paused.TrySetResult();
            }
        };

        var startTask = engine.StartAsync(items, delayBetween: TimeSpan.Zero, rateLimitDelay: TimeSpan.FromSeconds(1));

        await tcsItem2Paused.Task;
        Assert.Equal(BulkOperationState.Paused, engine.State);

        // Resume after pausing
        await Task.Delay(50);
        engine.Resume();

        await startTask;

        Assert.Equal(BulkOperationState.Completed, engine.State);
        Assert.Equal(4, fakeTelegram.LeaveCalls.Count);
        Assert.All(items, item => Assert.False(item.IsJoined));
    }

    [Fact]
    public async Task Stop_InterruptsExecutionCleanly()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        var items = new List<DialogItem>
        {
            new DialogItem { Id = -301, Title = "Channel 1", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -302, Title = "Channel 2", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -303, Title = "Channel 3", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -304, Title = "Channel 4", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -305, Title = "Channel 5", DialogType = DialogType.Channel, IsJoined = true }
        };

        engine.ProgressChanged += (_, report) =>
        {
            if (report.CurrentIndex == 2)
            {
                engine.Stop();
            }
        };

        await engine.StartAsync(items, delayBetween: TimeSpan.Zero, rateLimitDelay: TimeSpan.FromSeconds(1));

        Assert.Equal(BulkOperationState.Interrupted, engine.State);
        Assert.True(fakeTelegram.LeaveCalls.Count <= 3);
        Assert.True(fakeTelegram.LeaveCalls.Count < items.Count);
    }

    [Fact]
    public async Task Cancellation_InterruptsExecutionCleanly()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        var items = new List<DialogItem>
        {
            new DialogItem { Id = -401, Title = "Channel 1", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -402, Title = "Channel 2", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -403, Title = "Channel 3", DialogType = DialogType.Channel, IsJoined = true }
        };

        using var cts = new CancellationTokenSource();

        engine.ProgressChanged += (_, report) =>
        {
            if (report.CurrentIndex == 1)
            {
                cts.Cancel();
            }
        };

        await engine.StartAsync(items, delayBetween: TimeSpan.FromSeconds(2), rateLimitDelay: TimeSpan.FromSeconds(1), ct: cts.Token);

        Assert.Equal(BulkOperationState.Interrupted, engine.State);
        Assert.True(fakeTelegram.LeaveCalls.Count < items.Count);
    }

    [Fact]
    public async Task EventReporting_ProgressChangedAndLogEntryAdded()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        var items = new List<DialogItem>
        {
            new DialogItem { Id = -501, Title = "Alpha", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -502, Title = "Beta", DialogType = DialogType.Channel, IsJoined = true }
        };

        var progressReports = new List<BulkProgressReport>();
        var logEntries = new List<BulkLogEntry>();

        engine.ProgressChanged += (_, report) => progressReports.Add(report);
        engine.LogEntryAdded += (_, entry) => logEntries.Add(entry);

        await engine.StartAsync(items, delayBetween: TimeSpan.Zero, rateLimitDelay: TimeSpan.FromSeconds(1));

        Assert.NotEmpty(progressReports);
        Assert.NotEmpty(logEntries);

        // Verification of progress reports
        var finalProgress = progressReports.Last();
        Assert.Equal(BulkOperationState.Completed, finalProgress.State);
        Assert.Equal(2, finalProgress.SuccessCount);
        Assert.Equal(0, finalProgress.ErrorCount);
        Assert.Equal(100.0, finalProgress.Percent);

        // Verification of log entries
        Assert.Contains(logEntries, e => e.DialogId == -501 && e.Result == LeaveResult.Success);
        Assert.Contains(logEntries, e => e.DialogId == -502 && e.Result == LeaveResult.Success);
        Assert.Contains(logEntries, e => e.Message.Contains("Операция завершена"));

        // Verification that logs were persisted to cache service
        Assert.Equal(2, fakeCache.History.Count);
    }

    [Fact]
    public async Task PreventConcurrentStart_ThrowsInvalidOperationException()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        var items = new List<DialogItem>
        {
            new DialogItem { Id = -601, Title = "Channel 1", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = -602, Title = "Channel 2", DialogType = DialogType.Channel, IsJoined = true }
        };

        var runningTask = engine.StartAsync(items, delayBetween: TimeSpan.FromSeconds(5), rateLimitDelay: TimeSpan.FromSeconds(1));

        await Task.Delay(100);
        Assert.Equal(BulkOperationState.Running, engine.State);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.StartAsync(items, delayBetween: TimeSpan.Zero, rateLimitDelay: TimeSpan.FromSeconds(1)));

        engine.Stop();
        await runningTask;
    }
}
