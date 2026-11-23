namespace TeleClean.App.Models;

/// <summary>
/// Theme-independent semantic tone for status pills and badges.
/// Actual colors are always resolved via ThemeResources so Light, Dark, and HighContrast switch cleanly.
/// </summary>
public enum StatusTone
{
    Neutral = 0,
    Accent = 1,
    Success = 2,
    Warning = 3,
    Danger = 4
}
