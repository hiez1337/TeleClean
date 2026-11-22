using System.Text.Json;
using TeleClean.Core.Models;
using TeleClean.Tests.Fixtures;
using Xunit;

namespace TeleClean.Tests;

public class ClassificationTests
{
    [Fact]
    public void DialogType_EnumValues_ContainAllSixTypes()
    {
        var values = Enum.GetValues<DialogType>();
        Assert.Equal(6, values.Length);
        Assert.Contains(DialogType.Channel, values);
        Assert.Contains(DialogType.Supergroup, values);
        Assert.Contains(DialogType.Group, values);
        Assert.Contains(DialogType.Bot, values);
        Assert.Contains(DialogType.User, values);
        Assert.Contains(DialogType.Deleted, values);
    }

    [Theory]
    [InlineData(DialogType.Channel, true, true)]
    [InlineData(DialogType.Supergroup, false, true)]
    [InlineData(DialogType.Group, false, true)]
    [InlineData(DialogType.Bot, false, true)]
    [InlineData(DialogType.User, false, true)]
    [InlineData(DialogType.Deleted, false, false)]
    public void DialogItem_Classification_AllSixTypesInitializedCorrectly(
        DialogType type,
        bool expectedIsChannel,
        bool expectedIsJoined)
    {
        var item = new DialogItem
        {
            Id = -1001,
            Title = $"Test {type}",
            DialogType = type,
            IsChannel = expectedIsChannel,
            IsJoined = expectedIsJoined
        };

        Assert.Equal(type, item.DialogType);
        Assert.Equal(expectedIsChannel, item.IsChannel);
        Assert.Equal(expectedIsJoined, item.IsJoined);
        Assert.False(string.IsNullOrWhiteSpace(item.ToString()));
    }

    [Theory]
    [InlineData(9223372036854775807L)] // long.MaxValue
    [InlineData(-9223372036854775808L)] // long.MinValue
    [InlineData(-1001987654321098L)] // Telegram 64-bit supergroup / channel ID
    [InlineData(1234567890123456L)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void DialogItem_64BitIds_PreservesFullRangeWithoutOverflow(long id)
    {
        var item = new DialogItem
        {
            Id = id,
            AccessHash = id ^ 0x5555555555555555L,
            Title = $"Dialog {id}",
            DialogType = DialogType.Channel
        };

        Assert.Equal(id, item.Id);
        Assert.Equal(id ^ 0x5555555555555555L, item.AccessHash);

        var clone = item.Clone();
        Assert.Equal(id, clone.Id);
        Assert.Equal(item.AccessHash, clone.AccessHash);

        var target = new DialogItem();
        target.UpdateFrom(item);
        Assert.Equal(id, target.Id);
        Assert.Equal(item.AccessHash, target.AccessHash);
    }

    [Fact]
    public void SubtitleFormatting_Channel_FormatsCorrectly()
    {
        var withAll = new DialogItem
        {
            DialogType = DialogType.Channel,
            ParticipantCount = 15000,
            UnreadCount = 3,
            Username = "technews"
        };
        var subtitleAll = withAll.Subtitle;
        Assert.Contains("👤", subtitleAll);
        Assert.Contains("15", subtitleAll);
        Assert.Contains("💬", subtitleAll);
        Assert.Contains("3", subtitleAll);
        Assert.Contains("@technews", subtitleAll);

        var withCountOnly = new DialogItem
        {
            DialogType = DialogType.Channel,
            ParticipantCount = 500,
            UnreadCount = 0,
            Username = null
        };
        var subtitleCount = withCountOnly.Subtitle;
        Assert.Contains("👤", subtitleCount);
        Assert.Contains("500", subtitleCount);
        Assert.DoesNotContain("💬", subtitleCount);
        Assert.DoesNotContain("@", subtitleCount);

        var empty = new DialogItem
        {
            DialogType = DialogType.Channel,
            ParticipantCount = 0,
            UnreadCount = 0,
            Username = null
        };
        Assert.Equal("Нет данных", empty.Subtitle);
    }

    [Theory]
    [InlineData(DialogType.Supergroup)]
    [InlineData(DialogType.Group)]
    public void SubtitleFormatting_SupergroupAndGroup_UsesGroupIcon(DialogType type)
    {
        var item = new DialogItem
        {
            DialogType = type,
            ParticipantCount = 120,
            UnreadCount = 5,
            Username = "chat_group"
        };
        var subtitle = item.Subtitle;
        Assert.Contains("👥", subtitle);
        Assert.Contains("120", subtitle);
        Assert.Contains("💬", subtitle);
        Assert.Contains("5", subtitle);
        Assert.Contains("@chat_group", subtitle);
    }

    [Fact]
    public void SubtitleFormatting_Bot_FormatsCorrectly()
    {
        var botWithUsername = new DialogItem
        {
            DialogType = DialogType.Bot,
            Username = "helper_bot"
        };
        Assert.Equal("🤖 @helper_bot", botWithUsername.Subtitle);

        var botWithoutUsername = new DialogItem
        {
            DialogType = DialogType.Bot,
            Username = null
        };
        Assert.Equal("🤖 Бот", botWithoutUsername.Subtitle);
    }

    [Fact]
    public void SubtitleFormatting_Deleted_FormatsCorrectly()
    {
        var deleted = new DialogItem
        {
            DialogType = DialogType.Deleted,
            Title = "Deleted Account"
        };
        Assert.Equal("🗑 Удалённый аккаунт", deleted.Subtitle);
    }

    [Fact]
    public void SubtitleFormatting_User_FormatsCorrectly()
    {
        var userWithUsername = new DialogItem
        {
            DialogType = DialogType.User,
            Username = "johndoe"
        };
        Assert.Equal("👤 @johndoe", userWithUsername.Subtitle);

        var userWithoutUsername = new DialogItem
        {
            DialogType = DialogType.User,
            Username = null
        };
        Assert.Equal("👤 Пользователь", userWithoutUsername.Subtitle);
    }

    [Theory]
    [InlineData(DialogType.Channel, "channel")]
    [InlineData(DialogType.Supergroup, "supergroup")]
    [InlineData(DialogType.Group, "group")]
    [InlineData(DialogType.Bot, "bot")]
    [InlineData(DialogType.User, "user")]
    [InlineData(DialogType.Deleted, "deleted")]
    public void DialogTypeJsonConverter_SerializesToLowercaseStringsMatchingPython(DialogType type, string expectedJsonString)
    {
        var json = JsonSerializer.Serialize(type);
        Assert.Equal($"\"{expectedJsonString}\"", json);
    }

    [Theory]
    [InlineData("\"channel\"", DialogType.Channel)]
    [InlineData("\"supergroup\"", DialogType.Supergroup)]
    [InlineData("\"group\"", DialogType.Group)]
    [InlineData("\"bot\"", DialogType.Bot)]
    [InlineData("\"user\"", DialogType.User)]
    [InlineData("\"deleted\"", DialogType.Deleted)]
    [InlineData("\"CHANNEL\"", DialogType.Channel)]
    [InlineData("\"SuperGroup\"", DialogType.Supergroup)]
    [InlineData("\"Deleted\"", DialogType.Deleted)]
    public void DialogTypeJsonConverter_DeserializesFromLowercaseStringsMatchingPython(string json, DialogType expectedType)
    {
        var deserialized = JsonSerializer.Deserialize<DialogType>(json);
        Assert.Equal(expectedType, deserialized);
    }

    [Theory]
    [InlineData("0", DialogType.Channel)]
    [InlineData("1", DialogType.Supergroup)]
    [InlineData("2", DialogType.Group)]
    [InlineData("3", DialogType.Bot)]
    [InlineData("4", DialogType.User)]
    [InlineData("5", DialogType.Deleted)]
    public void DialogTypeJsonConverter_DeserializesFromNumbers(string jsonNumber, DialogType expectedType)
    {
        var deserialized = JsonSerializer.Deserialize<DialogType>(jsonNumber);
        Assert.Equal(expectedType, deserialized);
    }

    [Fact]
    public void DialogItem_JsonSerialization_RoundTripPreservesAllFields()
    {
        var sample = SampleDialogs.CreateExtendedDialogsWithCyrillicAnd64Bit();

        var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<List<DialogItem>>(json);

        Assert.NotNull(restored);
        Assert.Equal(sample.Count, restored.Count);

        for (int i = 0; i < sample.Count; i++)
        {
            var original = sample[i];
            var item = restored[i];

            Assert.Equal(original.Id, item.Id);
            Assert.Equal(original.AccessHash, item.AccessHash);
            Assert.Equal(original.Title, item.Title);
            Assert.Equal(original.Username, item.Username);
            Assert.Equal(original.ParticipantCount, item.ParticipantCount);
            Assert.Equal(original.UnreadCount, item.UnreadCount);
            Assert.Equal(original.IsChannel, item.IsChannel);
            Assert.Equal(original.IsJoined, item.IsJoined);
            Assert.Equal(original.DialogType, item.DialogType);
            Assert.Equal(original.LastActivity, item.LastActivity);
            Assert.Equal(original.IsActive, item.IsActive);
        }
    }

    [Fact]
    public void DialogItem_CloneAndUpdateFrom_CopiesLastActivityAndIsActive()
    {
        var original = new DialogItem
        {
            Id = -100998877,
            Title = "Clone Test",
            DialogType = DialogType.Channel,
            LastActivity = new DateTime(2026, 3, 1, 14, 30, 0, DateTimeKind.Utc),
            IsActive = false
        };

        var cloned = original.Clone();
        Assert.Equal(original.LastActivity, cloned.LastActivity);
        Assert.False(cloned.IsActive);

        var target = new DialogItem();
        target.UpdateFrom(original);
        Assert.Equal(original.LastActivity, target.LastActivity);
        Assert.False(target.IsActive);
    }

    [Theory]
    [InlineData(LeaveResult.Success, "Успешно")]
    [InlineData(LeaveResult.RateLimited, "FloodWait")]
    [InlineData(LeaveResult.Forbidden, "Запрещено")]
    [InlineData(LeaveResult.Error, "Ошибка")]
    public void BulkLogEntry_StatusTextAndAction_ReturnsExpectedValues(LeaveResult result, string expectedStatusText)
    {
        var timestamp = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var entry = new BulkLogEntry(timestamp, -1001, "Test Channel", result, "Details", "Выход из канала");

        Assert.Equal(result, entry.Result);
        Assert.Equal("Выход из канала", entry.Action);
        Assert.Equal(expectedStatusText, entry.StatusText);
        Assert.False(string.IsNullOrWhiteSpace(entry.FormattedTime));
    }
}
