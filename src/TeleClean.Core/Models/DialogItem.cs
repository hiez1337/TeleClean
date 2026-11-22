using System.Text.Json.Serialization;

namespace TeleClean.Core.Models;

/// <summary>
/// Domain model representing a Telegram dialog (channel, supergroup, group, bot, user, or deleted account).
/// </summary>
public sealed class DialogItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("access_hash")]
    public long AccessHash { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("participant_count")]
    public int ParticipantCount { get; set; }

    [JsonPropertyName("unread_count")]
    public int UnreadCount { get; set; }

    [JsonPropertyName("is_channel")]
    public bool IsChannel { get; set; } = true;

    [JsonPropertyName("is_joined")]
    public bool IsJoined { get; set; } = true;

    [JsonPropertyName("dialog_type")]
    public DialogType DialogType { get; set; } = DialogType.Channel;

    [JsonPropertyName("last_activity")]
    public DateTime? LastActivity { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonIgnore]
    public bool IsSelected { get; set; }

    [JsonIgnore]
    public string? StatusBadge { get; set; }

    [JsonIgnore]
    public string? AvatarLocalPath { get; set; }

    /// <summary>
    /// User-facing subtitle formatted with member count, unread count, and username.
    /// </summary>
    [JsonIgnore]
    public string Subtitle => DialogType switch
    {
        DialogType.Channel => FormatSubtitle("👤", ParticipantCount, "💬", UnreadCount, Username),
        DialogType.Supergroup or DialogType.Group => FormatSubtitle("👥", ParticipantCount, "💬", UnreadCount, Username),
        DialogType.Bot => string.IsNullOrWhiteSpace(Username) ? "🤖 Бот" : $"🤖 @{Username}",
        DialogType.Deleted => "🗑 Удалённый аккаунт",
        DialogType.User => string.IsNullOrWhiteSpace(Username) ? "👤 Пользователь" : $"👤 @{Username}",
        _ => string.Empty
    };

    private static string FormatSubtitle(string memberIcon, int members, string unreadIcon, int unread, string? username)
    {
        var parts = new List<string>();
        if (members > 0) parts.Add($"{memberIcon} {members:N0}");
        if (unread > 0) parts.Add($"{unreadIcon} {unread:N0}");
        if (!string.IsNullOrWhiteSpace(username)) parts.Add($"@{username}");
        return parts.Count > 0 ? string.Join(" | ", parts) : "Нет данных";
    }

    /// <summary>
    /// Creates a deep clone of this DialogItem.
    /// </summary>
    public DialogItem Clone()
    {
        return new DialogItem
        {
            Id = Id,
            AccessHash = AccessHash,
            Title = Title,
            Username = Username,
            ParticipantCount = ParticipantCount,
            UnreadCount = UnreadCount,
            IsChannel = IsChannel,
            IsJoined = IsJoined,
            DialogType = DialogType,
            IsSelected = IsSelected,
            StatusBadge = StatusBadge,
            AvatarLocalPath = AvatarLocalPath,
            LastActivity = LastActivity,
            IsActive = IsActive
        };
    }

    /// <summary>
    /// Updates properties from another DialogItem instance.
    /// </summary>
    public void UpdateFrom(DialogItem other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Id = other.Id;
        AccessHash = other.AccessHash;
        Title = other.Title;
        Username = other.Username;
        ParticipantCount = other.ParticipantCount;
        UnreadCount = other.UnreadCount;
        IsChannel = other.IsChannel;
        IsJoined = other.IsJoined;
        DialogType = other.DialogType;
        IsSelected = other.IsSelected;
        StatusBadge = other.StatusBadge;
        AvatarLocalPath = other.AvatarLocalPath ?? AvatarLocalPath;
        LastActivity = other.LastActivity ?? LastActivity;
        IsActive = other.IsActive;
    }

    public override string ToString()
    {
        return $"{Title} ({DialogType}, Id={Id})";
    }
}
