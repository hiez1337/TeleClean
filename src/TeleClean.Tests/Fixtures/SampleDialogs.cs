using TeleClean.Core.Models;

namespace TeleClean.Tests.Fixtures;

/// <summary>
/// Provides shared test fixtures matching Python's sample_channels fixture with additional 64-bit edge cases.
/// </summary>
public static class SampleDialogs
{
    public static List<DialogItem> CreateSampleChannels()
    {
        return new List<DialogItem>
        {
            new DialogItem
            {
                Id = -1001,
                Title = "Tech News",
                Username = "technews",
                ParticipantCount = 15000,
                UnreadCount = 3,
                IsChannel = true,
                IsJoined = true,
                DialogType = DialogType.Channel
            },
            new DialogItem
            {
                Id = -1002,
                Title = "Cooking Recipes",
                Username = null,
                ParticipantCount = 500,
                UnreadCount = 0,
                IsChannel = true,
                IsJoined = true,
                DialogType = DialogType.Channel
            },
            new DialogItem
            {
                Id = -1003,
                Title = "Abandoned Channel",
                Username = "dead",
                ParticipantCount = 15,
                UnreadCount = 0,
                IsChannel = true,
                IsJoined = true,
                DialogType = DialogType.Channel
            },
            new DialogItem
            {
                Id = -1004,
                Title = "Python Developers",
                Username = "pythondev",
                ParticipantCount = 4200,
                UnreadCount = 12,
                IsChannel = true,
                IsJoined = true,
                DialogType = DialogType.Channel
            },
            new DialogItem
            {
                Id = -1005,
                Title = "Music Lovers",
                Username = "music",
                ParticipantCount = 80,
                UnreadCount = 1,
                IsChannel = true,
                IsJoined = true,
                DialogType = DialogType.Channel
            },
            new DialogItem
            {
                Id = -1006,
                Title = "Empty Channel",
                Username = null,
                ParticipantCount = 0,
                UnreadCount = 0,
                IsChannel = true,
                IsJoined = true,
                DialogType = DialogType.Channel
            },
            new DialogItem
            {
                Id = -2001,
                Title = "Dev Chat",
                Username = "devchat",
                ParticipantCount = 120,
                UnreadCount = 5,
                IsChannel = false,
                IsJoined = true,
                DialogType = DialogType.Supergroup
            },
            new DialogItem
            {
                Id = -2002,
                Title = "Old Group",
                Username = null,
                ParticipantCount = 8,
                UnreadCount = 0,
                IsChannel = false,
                IsJoined = true,
                DialogType = DialogType.Group
            },
            new DialogItem
            {
                Id = -3001,
                Title = "Test Bot",
                Username = "testbot",
                ParticipantCount = 0,
                UnreadCount = 2,
                IsChannel = false,
                IsJoined = true,
                DialogType = DialogType.Bot
            },
            new DialogItem
            {
                Id = -4001,
                Title = "Deleted User",
                Username = null,
                ParticipantCount = 0,
                UnreadCount = 0,
                IsChannel = false,
                IsJoined = true,
                DialogType = DialogType.Deleted
            }
        };
    }

    public static List<DialogItem> CreateExtendedDialogsWithCyrillicAnd64Bit()
    {
        var items = CreateSampleChannels();
        items.AddRange(new[]
        {
            new DialogItem
            {
                Id = 9223372036854775807L, // long.MaxValue
                AccessHash = 888777666555444L,
                Title = "Новости Высоких Технологий",
                Username = "hi_tech_ru",
                ParticipantCount = 95000,
                UnreadCount = 24,
                IsChannel = true,
                IsJoined = true,
                DialogType = DialogType.Channel
            },
            new DialogItem
            {
                Id = -1001987654321098L, // large negative 64-bit ID
                AccessHash = 111222333444555L,
                Title = "Русский Чат Разработчиков",
                Username = "ru_devs",
                ParticipantCount = 350,
                UnreadCount = 7,
                IsChannel = false,
                IsJoined = true,
                DialogType = DialogType.Supergroup
            },
            new DialogItem
            {
                Id = 1234567890123L,
                AccessHash = 999888777666555L,
                Title = "Алексей Смирнов",
                Username = "alex_smirnov",
                ParticipantCount = 0,
                UnreadCount = 0,
                IsChannel = false,
                IsJoined = true,
                DialogType = DialogType.User
            }
        });
        return items;
    }
}
