using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TeleClean.Tests.Mocks;
using Xunit;

namespace TeleClean.Tests;

public class RateLimitBackoffTests
{
    [Fact]
    public async Task RateLimitBackoff_TransitionsToRateLimited_CountsDownAndResumes()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        var item1 = new DialogItem { Id = -1001, Title = "Flood Channel", DialogType = DialogType.Channel, IsJoined = true };
        var item2 = new DialogItem { Id = -1002, Title = "Safe Channel", DialogType = DialogType.Channel, IsJoined = true };
        var items = new List<DialogItem> { item1, item2 };

        fakeTelegram.SetLeaveResult(-1001, LeaveResult.RateLimited);
        fakeTelegram.SetLeaveResult(-1002, LeaveResult.Success);

        var rateLimitReports = new List<BulkProgressReport>();
        var logs = new List<BulkLogEntry>();

        engine.ProgressChanged += (_, report) =>
        {
            if (report.State == BulkOperationState.RateLimited)
            {
                rateLimitReports.Add(report);
            }
        };

        engine.LogEntryAdded += (_, entry) => logs.Add(entry);

        // 2 seconds rate-limit delay
        await engine.StartAsync(items, delayBetween: TimeSpan.Zero, rateLimitDelay: TimeSpan.FromSeconds(2));

        Assert.Equal(BulkOperationState.Completed, engine.State);

        // Verify countdown reports occurred
        Assert.NotEmpty(rateLimitReports);
        var remainingSeconds = rateLimitReports.Select(r => r.SecondsRemaining).ToList();
        Assert.Contains(2, remainingSeconds);
        Assert.Contains(1, remainingSeconds);

        // Verify items status
        Assert.Equal("⏳ Ограничение", item1.StatusBadge);
        Assert.Equal("✅ Вышел", item2.StatusBadge);
        Assert.True(item1.IsJoined); // Still joined due to rate limit failure
        Assert.False(item2.IsJoined);

        // Verify log entries recorded rate limit
        Assert.Contains(logs, l => l.DialogId == -1001 && l.Result == LeaveResult.RateLimited);
        Assert.Contains(logs, l => l.DialogId == -1002 && l.Result == LeaveResult.Success);
    }

    [Fact]
    public async Task RateLimitBackoff_StopDuringCountdown_ImmediatelyInterrupts()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        var item = new DialogItem { Id = -2001, Title = "Rate Limited Channel", DialogType = DialogType.Channel, IsJoined = true };
        fakeTelegram.SetLeaveResult(-2001, LeaveResult.RateLimited);

        engine.ProgressChanged += (_, report) =>
        {
            if (report.State == BulkOperationState.RateLimited)
            {
                // Trigger Stop immediately when backoff starts
                engine.Stop();
            }
        };

        var start = DateTime.UtcNow;
        // Large backoff (30 seconds)
        await engine.StartAsync(new[] { item }, delayBetween: TimeSpan.Zero, rateLimitDelay: TimeSpan.FromSeconds(30));
        var elapsed = DateTime.UtcNow - start;

        Assert.Equal(BulkOperationState.Interrupted, engine.State);
        // Must break immediately without waiting full 30 seconds
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"Expected fast abort, but took {elapsed.TotalSeconds}s");
    }

    [Fact]
    public async Task RateLimitBackoff_CancellationDuringCountdown_ImmediatelyInterrupts()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        var item = new DialogItem { Id = -3001, Title = "Canceled Channel", DialogType = DialogType.Channel, IsJoined = true };
        fakeTelegram.SetLeaveResult(-3001, LeaveResult.RateLimited);

        using var cts = new CancellationTokenSource();

        engine.ProgressChanged += (_, report) =>
        {
            if (report.State == BulkOperationState.RateLimited)
            {
                cts.Cancel();
            }
        };

        var start = DateTime.UtcNow;
        await engine.StartAsync(new[] { item }, delayBetween: TimeSpan.Zero, rateLimitDelay: TimeSpan.FromSeconds(30), ct: cts.Token);
        var elapsed = DateTime.UtcNow - start;

        Assert.Equal(BulkOperationState.Interrupted, engine.State);
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"Expected fast abort on cancellation, but took {elapsed.TotalSeconds}s");
    }
}
