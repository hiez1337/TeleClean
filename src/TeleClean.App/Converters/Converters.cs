using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using TeleClean.App.Models;
using TeleClean.Core.Models;

namespace TeleClean.App.Converters;

/// <summary>
/// Converts bool, int, string, or reference to inverted Visibility
/// (true/positive/non-empty = Collapsed, false/0/empty/null = Visible).
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
/// Converts positive integer counts (> 0) to Visible, and <= 0 to Collapsed.
/// </summary>
public sealed class PositiveIntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts zero integer counts (<= 0) to Visible, and > 0 to Collapsed.
/// </summary>
public sealed class ZeroIntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int n && n > 0 ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a dimmed boolean flag into reduced opacity (0.58 when dimmed, 1.0 when active).
/// </summary>
public sealed class DimmedToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? 0.58 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a BulkLogEntry to a theme-independent StatusTone (distinguishing summary entries from per-item errors).
/// </summary>
public sealed class LogEntryToToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is BulkLogEntry entry)
        {
            if (entry.DialogId == 0)
            {
                return entry.Result == LeaveResult.Success ? StatusTone.Success : StatusTone.Warning;
            }

            return entry.Result switch
            {
                LeaveResult.Success => StatusTone.Success,
                LeaveResult.RateLimited => StatusTone.Warning,
                LeaveResult.Forbidden or LeaveResult.NotFound or LeaveResult.Error => StatusTone.Danger,
                _ => StatusTone.Neutral
            };
        }

        return StatusTone.Neutral;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a BulkLogEntry to a clean semantic badge label (showing "Итог" / "Прервано" for summary records).
/// </summary>
public sealed class LogEntryToBadgeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is BulkLogEntry entry)
        {
            if (entry.DialogId == 0)
            {
                return entry.Result == LeaveResult.Success ? "Итог" : "Прервано";
            }

            return entry.StatusText;
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Strips redundant leading status emojis from journal log messages so semantic StatusBadges stay uncluttered.
/// </summary>
public sealed class CleanLogMessageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string trimmed = text.Trim();
        foreach (string prefix in new[] { "✅ ", "⏳ ", "🗑 ", "✓ " })
        {
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }
        return trimmed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
