using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using TeleClean.Core.Models;

namespace TeleClean.App.ViewModels;

/// <summary>
/// Observable wrapper around TeleClean.Core.Models.DialogItem.
/// Enables two-way checkbox binding, real-time counters, and post-action visual states.
/// </summary>
public sealed partial class DialogItemViewModel : ObservableObject
{
    public DialogItem Model { get; }

    public long Id => Model.Id;
    public string Title => Model.Title;
    public string? Username => Model.Username;
    public int ParticipantCount => Model.ParticipantCount;
    public int UnreadCount => Model.UnreadCount;
    public DialogType DialogType => Model.DialogType;
    public string ParticipantsText => ParticipantCount > 0 ? ParticipantCount.ToString("N0") : "—";
    public string ActivityDateText => LastActivity?.ToLocalTime().ToString("dd.MM.yyyy") ?? "—";
    public string DisplayStatus => StatusBadge ?? StatusText;

    private bool _showComparison;
    public bool ShowComparison
    {
        get => _showComparison;
        set
        {
            if (!SetProperty(ref _showComparison, value)) return;
            OnPropertyChanged(nameof(ShowCompactDetails));
            OnPropertyChanged(nameof(ShowComparisonUsername));
        }
    }

    public bool ShowCompactDetails => !ShowComparison;
    public bool ShowComparisonUsername => ShowComparison && !string.IsNullOrWhiteSpace(Username);

    public string Description
    {
        get
        {
            var parts = new System.Collections.Generic.List<string> { CategoryText };
            if (!string.IsNullOrWhiteSpace(Username)) parts.Add($"@{Username}");
            if (ParticipantCount > 0) parts.Add($"{ParticipantCount:N0} уч.");
            if (UnreadCount > 0) parts.Add($"Новых: {UnreadCount}");
            if (LastActivity.HasValue) parts.Add(FormattedLastActivity);
            return string.Join("  ·  ", parts);
        }
    }
    public string Subtitle => Model.Subtitle;
    public DateTime? LastActivity => Model.LastActivity;
    public bool IsActive => Model.IsActive && IsJoined;
    public string StatusText => IsActive ? "Активен" : "Неактивен";
    public string UsernameFormatted => !string.IsNullOrWhiteSpace(Username) ? $"@{Username}" : string.Empty;

    public string CategoryText => DialogType switch
    {
        DialogType.Channel => "Канал",
        DialogType.Supergroup or DialogType.Group => "Чат",
        DialogType.Bot => "Бот",
        DialogType.Deleted => "Удалённый",
        DialogType.User => "Пользователь",
        _ => "Диалог"
    };

    public string FormattedSubscribers => FormatSubscribers(ParticipantCount, DialogType);
    public string SubscribersText => FormattedSubscribers;
    public string FormattedLastActivity => FormatRelativeTime(LastActivity);

    public static string FormatSubscribers(int count, DialogType type)
    {
        if (type is DialogType.Bot or DialogType.Deleted or DialogType.User)
            return "—";
        if (count <= 0) return "—";
        if (count >= 1_000_000)
        {
            double m = count / 1_000_000.0;
            return m.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";
        }
        if (count >= 1_000)
        {
            double k = count / 1_000.0;
            return k.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K";
        }
        return count.ToString("N0");
    }

    public static string FormatRelativeTime(DateTime? dateTime)
    {
        if (!dateTime.HasValue) return "Давно";
        var dt = dateTime.Value.ToLocalTime();
        var now = DateTime.Now;
        var span = now - dt;

        if (span.TotalMinutes < 1) return "Только что";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} мин. назад";
        if (dt.Date == now.Date) return $"Сегодня {dt:HH:mm}";
        if (dt.Date == now.Date.AddDays(-1)) return $"Вчера {dt:HH:mm}";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} дн. назад";
        if (span.TotalDays < 30)
        {
            int weeks = Math.Max(1, (int)(span.TotalDays / 7));
            return $"{weeks} нед. назад";
        }
        if (span.TotalDays < 365)
        {
            int months = Math.Max(1, (int)(span.TotalDays / 30));
            return $"{months} мес. назад";
        }

        int years = Math.Max(1, (int)(span.TotalDays / 365));
        return years == 1 ? "1 год назад" : $"{years} г. назад";
    }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    [NotifyPropertyChangedFor(nameof(IsNotJoined))]
    [NotifyPropertyChangedFor(nameof(IsDimmed))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private bool _isJoined;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    private bool _selectionEnabled;

    public bool CanToggle => SelectionEnabled && IsJoined;
    public bool IsNotJoined => !IsJoined;
    public bool IsDimmed => !IsJoined;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private string? _statusBadge;

    [ObservableProperty]
    private string? _avatarLocalPath;

    [ObservableProperty]
    private ImageSource? _avatarImage;

    public event Action<DialogItemViewModel>? SelectionChanged;

    public DialogItemViewModel(DialogItem model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _isSelected = model.IsSelected;
        _isJoined = model.IsJoined;
        _statusBadge = model.StatusBadge;
        _avatarLocalPath = model.AvatarLocalPath;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        Model.IsSelected = value;
        SelectionChanged?.Invoke(this);
    }

    /// <summary>
    /// Updates item to post-action state («✅ Вышел» or «🗑 Удален» badge, strikethrough, unchecked).
    /// </summary>
    public void MarkLeft(string? badge = null)
    {
        string finalBadge = badge ?? "✅ Вышел";
        IsJoined = false;
        IsSelected = false;
        StatusBadge = finalBadge;
        Model.IsJoined = false;
        Model.IsSelected = false;
        Model.StatusBadge = finalBadge;
    }
}
