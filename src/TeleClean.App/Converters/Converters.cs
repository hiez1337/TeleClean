using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using TeleClean.Core.Models;
using Windows.UI;

namespace TeleClean.App.Converters;

/// <summary>
/// Converts bool, int, string, or reference to Visibility.
/// When targetType is bool, returns truthy boolean evaluation.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isTrue = value switch
        {
            bool b => b,
            int n => n > 0,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => value != null
        };

        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            return isTrue;
        }

        return isTrue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// Converts bool, int, string, or reference to inverted Visibility (true/positive/non-empty = Collapsed, false/0/empty/null = Visible).
/// When targetType is bool, returns boolean negation.
/// </summary>
public sealed class InvertedBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isTrue = value switch
        {
            bool b => b,
            int n => n > 0,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => value != null
        };

        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            return !isTrue;
        }

        return isTrue ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility v && v != Visibility.Visible;
}

/// <summary>
/// Inverts a boolean value (true -> false, false -> true) or truthiness.
/// </summary>
public sealed class BooleanInversionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value switch
        {
            bool b => !b,
            int n => n <= 0,
            string s => string.IsNullOrWhiteSpace(s),
            _ => value == null
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is bool b ? !b : false;
}

/// <summary>
/// Converts integer count to Visibility (0 -> Visible, >0 -> Collapsed).
/// </summary>
public sealed class EmptyCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int n && n > 0 ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts null or non-null reference to Visibility.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts DialogType to emoji icon glyph.
/// </summary>
public sealed class DialogTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DialogType type)
        {
            return type switch
            {
                DialogType.Channel => "📺",
                DialogType.Supergroup or DialogType.Group => "👥",
                DialogType.Bot => "🤖",
                DialogType.Deleted => "🗑",
                DialogType.User => "👤",
                _ => "💬"
            };
        }
        return "💬";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts LeaveResult or status strings to theme color brushes.
/// </summary>
public sealed class StatusBadgeConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromArgb(255, 50, 229, 94));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromArgb(255, 241, 196, 15));
    private static readonly SolidColorBrush DangerBrush = new(Color.FromArgb(255, 231, 76, 60));
    private static readonly SolidColorBrush NeutralBrush = new(Color.FromArgb(255, 140, 140, 140));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LeaveResult result)
        {
            return result switch
            {
                LeaveResult.Success => SuccessBrush,
                LeaveResult.RateLimited => WarningBrush,
                LeaveResult.Forbidden or LeaveResult.Error => DangerBrush,
                _ => NeutralBrush
            };
        }
        return NeutralBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts LeaveResult to subtle badge background color brush.
/// </summary>
public sealed class StatusBadgeBgConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBg = new(Color.FromArgb(40, 50, 229, 94));
    private static readonly SolidColorBrush WarningBg = new(Color.FromArgb(40, 241, 196, 15));
    private static readonly SolidColorBrush DangerBg = new(Color.FromArgb(40, 231, 76, 60));
    private static readonly SolidColorBrush NeutralBg = new(Color.FromArgb(25, 140, 140, 140));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LeaveResult result)
        {
            return result switch
            {
                LeaveResult.Success => SuccessBg,
                LeaveResult.RateLimited => WarningBg,
                LeaveResult.Forbidden or LeaveResult.Error => DangerBg,
                _ => NeutralBg
            };
        }
        return NeutralBg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Formats integer numbers with digit grouping (e.g. 15 200).
/// </summary>
public sealed class CountFormattingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int n ? $"{n:N0}" : "0";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts DateTime to formatted time string (HH:mm:ss).
/// </summary>
public sealed class TimestampConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is DateTime dt ? dt.ToLocalTime().ToString("HH:mm:ss") : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts isJoined boolean to TextDecorations (strikethrough when not joined).
/// </summary>
public sealed class JoinedToTextDecorationsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isStrikethrough = (value is bool joined && !joined);
        if (targetType != null && targetType.IsEnum)
        {
            return Enum.ToObject(targetType, isStrikethrough ? 2 : 0);
        }
        return isStrikethrough ? "Strikethrough" : "None";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts BulkOperationState to status badge background brush.
/// </summary>
public sealed class BulkStateToBackgroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBg = new(Color.FromArgb(50, 46, 204, 113));
    private static readonly SolidColorBrush WarningBg = new(Color.FromArgb(50, 241, 196, 15));
    private static readonly SolidColorBrush RunningBg = new(Color.FromArgb(50, 41, 144, 255));
    private static readonly SolidColorBrush NeutralBg = new(Color.FromArgb(30, 128, 128, 128));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is BulkOperationState state)
        {
            return state switch
            {
                BulkOperationState.Running => RunningBg,
                BulkOperationState.Paused or BulkOperationState.RateLimited => WarningBg,
                BulkOperationState.Completed => SuccessBg,
                _ => NeutralBg
            };
        }
        return NeutralBg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts BulkOperationState to status badge foreground brush.
/// </summary>
public sealed class BulkStateToForegroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessFg = new(Color.FromArgb(255, 46, 204, 113));
    private static readonly SolidColorBrush WarningFg = new(Color.FromArgb(255, 241, 196, 15));
    private static readonly SolidColorBrush RunningFg = new(Color.FromArgb(255, 41, 144, 255));
    private static readonly SolidColorBrush NeutralFg = new(Color.FromArgb(255, 160, 160, 160));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is BulkOperationState state)
        {
            return state switch
            {
                BulkOperationState.Running => RunningFg,
                BulkOperationState.Paused or BulkOperationState.RateLimited => WarningFg,
                BulkOperationState.Completed => SuccessFg,
                _ => NeutralFg
            };
        }
        return NeutralFg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts row isSelected boolean to subtle dark-blue row highlight background brush.
/// </summary>
public sealed class RowSelectedBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush SelectedBg = new(Color.FromArgb(42, 41, 144, 255));
    private static readonly SolidColorBrush UnselectedBg = new(Color.FromArgb(0, 0, 0, 0));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (value is bool b && b) ? SelectedBg : UnselectedBg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts isActive boolean to foreground color brush (active = primary, inactive = muted secondary).
/// </summary>
public sealed class ActiveToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveFg = new(Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush InactiveFg = new(Color.FromArgb(160, 160, 160, 160));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (value is bool b && b) ? ActiveFg : InactiveFg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts unread count to badge background brush.
/// </summary>
public sealed class UnreadBadgeBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveUnreadBg = new(Color.FromArgb(40, 41, 144, 255));
    private static readonly SolidColorBrush InactiveUnreadBg = new(Color.FromArgb(0, 0, 0, 0));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (value is int count && count > 0) ? ActiveUnreadBg : InactiveUnreadBg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts unread count to badge foreground brush.
/// </summary>
public sealed class UnreadBadgeForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveUnreadFg = new(Color.FromArgb(255, 65, 175, 255));
    private static readonly SolidColorBrush InactiveUnreadFg = new(Color.FromArgb(140, 140, 140, 140));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (value is int count && count > 0) ? ActiveUnreadFg : InactiveUnreadFg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

