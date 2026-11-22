using TeleClean.Core.Models;

namespace TeleClean.Tests.Helpers;

/// <summary>
/// Extension methods for filtering and sorting dialog collections.
/// Encapsulates search, unread, low subscriber filters and Title/Activity/Participant sorting.
/// </summary>
public static class DialogFilterExtensions
{
    public static IEnumerable<DialogItem> Search(this IEnumerable<DialogItem> items, string? query)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (string.IsNullOrWhiteSpace(query))
        {
            return items;
        }

        var trimmed = query.Trim();
        return items.Where(d => d.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<DialogItem> FilterUnread(this IEnumerable<DialogItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(d => d.UnreadCount > 0);
    }

    public static IEnumerable<DialogItem> FilterLowSubscribers(this IEnumerable<DialogItem> items, int maxCount = 100)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(d => d.ParticipantCount <= maxCount);
    }

    public static IEnumerable<DialogItem> ApplyFilter(this IEnumerable<DialogItem> items, DialogFilter filter, int maxCount = 100)
    {
        ArgumentNullException.ThrowIfNull(items);

        return filter switch
        {
            DialogFilter.Unread => items.FilterUnread(),
            DialogFilter.LowSubscribers => items.FilterLowSubscribers(maxCount),
            _ => items
        };
    }

    public static IEnumerable<DialogItem> SortByTitle(this IEnumerable<DialogItem> items, bool descending = false)
    {
        ArgumentNullException.ThrowIfNull(items);

        return descending
            ? items.OrderByDescending(d => d.Title, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<DialogItem> SortByActivity(this IEnumerable<DialogItem> items, bool descending = true)
    {
        ArgumentNullException.ThrowIfNull(items);

        return descending
            ? items.OrderByDescending(d => d.UnreadCount).ThenByDescending(d => d.Title, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(d => d.UnreadCount).ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<DialogItem> SortByParticipants(this IEnumerable<DialogItem> items, bool descending = true)
    {
        ArgumentNullException.ThrowIfNull(items);

        return descending
            ? items.OrderByDescending(d => d.ParticipantCount).ThenByDescending(d => d.Title, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(d => d.ParticipantCount).ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<DialogItem> ApplySort(this IEnumerable<DialogItem> items, DialogSort sort, bool descending = false)
    {
        ArgumentNullException.ThrowIfNull(items);

        return sort switch
        {
            DialogSort.ByTitle => items.SortByTitle(descending),
            DialogSort.ByActivity => items.SortByActivity(descending: !descending),
            DialogSort.ByParticipants => items.SortByParticipants(descending: !descending),
            _ => items
        };
    }
}
