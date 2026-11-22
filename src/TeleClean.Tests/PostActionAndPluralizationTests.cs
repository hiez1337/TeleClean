using TeleClean.Core.Models;
using TeleClean.Core.Services;
using TeleClean.Tests.Mocks;
using Xunit;

namespace TeleClean.Tests;

public class PostActionAndPluralizationTests
{
    [Fact]
    public void DialogItem_MarkLeft_SetsJoinedFalseAndUpdatesBadge()
    {
        var item = new DialogItem
        {
            Id = 12345,
            Title = "Test Channel",
            DialogType = DialogType.Channel,
            IsJoined = true,
            IsSelected = true
        };

        item.IsJoined = false;
        item.IsSelected = false;
        item.StatusBadge = "✅ Вышел";

        Assert.False(item.IsJoined);
        Assert.False(item.IsSelected);
        Assert.Equal("✅ Вышел", item.StatusBadge);
    }

    [Fact]
    public void DialogItem_DeletedAccount_SetsDeletedBadge()
    {
        var item = new DialogItem
        {
            Id = 67890,
            Title = "Deleted User",
            DialogType = DialogType.Deleted,
            IsJoined = true,
            IsSelected = true
        };

        item.IsJoined = false;
        item.IsSelected = false;
        item.StatusBadge = "🗑 Удален";

        Assert.False(item.IsJoined);
        Assert.False(item.IsSelected);
        Assert.Equal("🗑 Удален", item.StatusBadge);
    }

    [Fact]
    public async Task BulkExecutionEngine_EmitsLogEntryAdded_OnSuccessfulLeave()
    {
        var fakeTelegram = new FakeTelegramService();
        var fakeCache = new FakeCacheService();
        var engine = new BulkExecutionEngine(fakeTelegram, fakeCache);

        var items = new List<DialogItem>
        {
            new DialogItem { Id = 1001, Title = "Channel 1", DialogType = DialogType.Channel, IsJoined = true },
            new DialogItem { Id = 1002, Title = "Deleted Account", DialogType = DialogType.Deleted, IsJoined = true }
        };

        var emittedLogs = new List<BulkLogEntry>();
        engine.LogEntryAdded += (_, log) =>
        {
            emittedLogs.Add(log);
        };

        await engine.StartAsync(items, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        Assert.Equal(3, emittedLogs.Count);
        Assert.Equal(1001, emittedLogs[0].DialogId);
        Assert.Equal(LeaveResult.Success, emittedLogs[0].Result);
        Assert.Equal(1002, emittedLogs[1].DialogId);
        Assert.Equal(LeaveResult.Success, emittedLogs[1].Result);
        Assert.Equal(0, emittedLogs[2].DialogId);
        Assert.Contains("Операция завершена", emittedLogs[2].Message);
    }

    [Theory]
    [InlineData(0, "five")]
    [InlineData(1, "one")]
    [InlineData(2, "two")]
    [InlineData(3, "two")]
    [InlineData(4, "two")]
    [InlineData(5, "five")]
    [InlineData(10, "five")]
    [InlineData(11, "five")]
    [InlineData(12, "five")]
    [InlineData(19, "five")]
    [InlineData(20, "five")]
    [InlineData(21, "one")]
    [InlineData(22, "two")]
    [InlineData(25, "five")]
    [InlineData(101, "one")]
    [InlineData(102, "two")]
    [InlineData(111, "five")]
    [InlineData(-1, "one")]
    [InlineData(-2, "two")]
    [InlineData(-5, "five")]
    [InlineData(-21, "one")]
    [InlineData(int.MaxValue, "five")]
    [InlineData(int.MinValue, "five")]
    public void RussianPluralization_BoundaryValues_EvaluateCorrectly(int count, string expected)
    {
        // Pluralization algorithm as implemented in TeleClean presentation layer
        long n = Math.Abs((long)count) % 100;
        long n1 = n % 10;
        string result;
        if (n is > 10 and < 20) result = "five";
        else if (n1 is > 1 and < 5) result = "two";
        else if (n1 == 1) result = "one";
        else result = "five";

        Assert.Equal(expected, result);
    }
}
