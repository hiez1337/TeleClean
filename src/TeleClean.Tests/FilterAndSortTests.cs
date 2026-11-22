using TeleClean.Core.Models;
using TeleClean.Tests.Fixtures;
using TeleClean.Tests.Helpers;
using Xunit;

namespace TeleClean.Tests;

public class FilterAndSortTests
{
    private readonly List<DialogItem> _sample = SampleDialogs.CreateSampleChannels();
    private readonly List<DialogItem> _extended = SampleDialogs.CreateExtendedDialogsWithCyrillicAnd64Bit();

    [Fact]
    public void Search_FiltersByTitle_Latin()
    {
        var result = _sample.Search("python").ToList();
        Assert.Single(result);
        Assert.Equal("Python Developers", result[0].Title);
    }

    [Fact]
    public void Search_CaseInsensitive()
    {
        var resultUpper = _sample.Search("TECH").ToList();
        Assert.Single(resultUpper);
        Assert.Equal("Tech News", resultUpper[0].Title);

        var resultLower = _sample.Search("tech").ToList();
        Assert.Single(resultLower);
        Assert.Equal("Tech News", resultLower[0].Title);
    }

    [Fact]
    public void Search_FiltersByTitle_Cyrillic()
    {
        var resultLower = _extended.Search("новости").ToList();
        Assert.Single(resultLower);
        Assert.Equal("Новости Высоких Технологий", resultLower[0].Title);

        var resultUpper = _extended.Search("НОВОСТИ").ToList();
        Assert.Single(resultUpper);
        Assert.Equal("Новости Высоких Технологий", resultUpper[0].Title);

        var resultSub = _extended.Search("чат").ToList();
        Assert.Contains(resultSub, d => d.Title == "Русский Чат Разработчиков");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Search_EmptyOrWhitespaceQuery_ReturnsAll(string? query)
    {
        var result = _sample.Search(query).ToList();
        Assert.Equal(_sample.Count, result.Count);
    }

    [Fact]
    public void Search_NonMatchingQuery_ReturnsEmpty()
    {
        var result = _sample.Search("NonExistentString12345!@#").ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void FilterUnread_ReturnsOnlyDialogsWithUnreadMessages()
    {
        var result = _sample.FilterUnread().ToList();
        Assert.Equal(5, result.Count); // Tech News (3), Python Devs (12), Music Lovers (1), Dev Chat (5), Test Bot (2)

        foreach (var item in result)
        {
            Assert.True(item.UnreadCount > 0);
        }

        Assert.Contains(result, d => d.Title == "Tech News");
        Assert.Contains(result, d => d.Title == "Python Developers");
        Assert.Contains(result, d => d.Title == "Music Lovers");
        Assert.Contains(result, d => d.Title == "Dev Chat");
        Assert.Contains(result, d => d.Title == "Test Bot");
        Assert.DoesNotContain(result, d => d.Title == "Cooking Recipes");
    }

    [Fact]
    public void FilterLowSubscribers_DefaultThreshold100()
    {
        var result = _sample.FilterLowSubscribers(100).ToList();
        // <= 100 subscribers: Abandoned (15), Music Lovers (80), Empty (0), Old Group (8), Test Bot (0), Deleted User (0)
        Assert.Equal(6, result.Count);

        foreach (var item in result)
        {
            Assert.True(item.ParticipantCount <= 100);
        }
    }

    [Fact]
    public void FilterLowSubscribers_CustomThreshold()
    {
        var result10 = _sample.FilterLowSubscribers(10).ToList();
        // <= 10 subscribers: Empty (0), Old Group (8), Test Bot (0), Deleted User (0)
        Assert.Equal(4, result10.Count);

        var result500 = _sample.FilterLowSubscribers(500).ToList();
        // <= 500: Cooking Recipes (500), Dev Chat (120), Abandoned (15), Music Lovers (80), Empty (0), Old Group (8), Test Bot (0), Deleted User (0)
        Assert.Equal(8, result500.Count);
    }

    [Fact]
    public void ApplyFilter_EnumMapping()
    {
        var all = _sample.ApplyFilter(DialogFilter.All).ToList();
        Assert.Equal(_sample.Count, all.Count);

        var unread = _sample.ApplyFilter(DialogFilter.Unread).ToList();
        Assert.Equal(5, unread.Count);

        var low = _sample.ApplyFilter(DialogFilter.LowSubscribers, 100).ToList();
        Assert.Equal(6, low.Count);
    }

    [Fact]
    public void SortByTitle_AscendingAZ()
    {
        var sorted = _sample.SortByTitle(descending: false).ToList();
        var titles = sorted.Select(s => s.Title).ToList();

        var expected = titles.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(expected, titles);
        Assert.Equal("Abandoned Channel", titles.First());
    }

    [Fact]
    public void SortByTitle_DescendingZA()
    {
        var sorted = _sample.SortByTitle(descending: true).ToList();
        var titles = sorted.Select(s => s.Title).ToList();

        var expected = titles.OrderByDescending(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(expected, titles);
    }

    [Fact]
    public void SortByActivity_UnreadDescendingTitleDescending()
    {
        var sorted = _sample.SortByActivity(descending: true).ToList();

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            Assert.True(sorted[i].UnreadCount >= sorted[i + 1].UnreadCount);
            if (sorted[i].UnreadCount == sorted[i + 1].UnreadCount)
            {
                Assert.True(string.Compare(sorted[i].Title, sorted[i + 1].Title, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        Assert.Equal("Python Developers", sorted.First().Title); // 12 unread
        Assert.Equal(12, sorted.First().UnreadCount);
    }

    [Fact]
    public void SortByParticipants_Descending()
    {
        var sorted = _sample.SortByParticipants(descending: true).ToList();

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            Assert.True(sorted[i].ParticipantCount >= sorted[i + 1].ParticipantCount);
        }

        Assert.Equal("Tech News", sorted.First().Title); // 15000 members
        Assert.Equal(15000, sorted.First().ParticipantCount);
    }

    [Fact]
    public void FilterAndSort_CombinedPipeline()
    {
        // Search "e" -> Tech News, Cooking Recipes, Abandoned Channel, Python Developers, Music Lovers, Empty Channel, Dev Chat, Test Bot, Deleted User
        // Filter unread -> Python Developers (12), Dev Chat (5), Tech News (3), Test Bot (2), Music Lovers (1)
        // Sort by Activity -> descending unread
        var pipelineResult = _sample
            .Search("e")
            .FilterUnread()
            .SortByActivity(descending: true)
            .ToList();

        Assert.Equal(5, pipelineResult.Count);
        Assert.Equal("Python Developers", pipelineResult[0].Title);
        Assert.Equal("Dev Chat", pipelineResult[1].Title);
        Assert.Equal("Tech News", pipelineResult[2].Title);
        Assert.Equal("Test Bot", pipelineResult[3].Title);
        Assert.Equal("Music Lovers", pipelineResult[4].Title);
    }
}
