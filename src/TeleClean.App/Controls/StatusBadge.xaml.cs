using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TeleClean.App.Models;
using Windows.UI.ViewManagement;

namespace TeleClean.App.Controls;

/// <summary>
/// Theme-aware semantic pill badge that dynamically re-resolves ThemeResources on ActualThemeChanged
/// and HighContrast changes.
/// </summary>
public sealed partial class StatusBadge : UserControl
{
    private readonly AccessibilitySettings _accessibility = new();

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBadge),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(StatusTone), typeof(StatusBadge),
            new PropertyMetadata(StatusTone.Neutral, OnVisualPropertyChanged));

    public static readonly DependencyProperty ShowDotProperty =
        DependencyProperty.Register(nameof(ShowDot), typeof(bool), typeof(StatusBadge),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StatusTone Tone
    {
        get => (StatusTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public bool ShowDot
    {
        get => (bool)GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }

    public StatusBadge()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyVisuals();
        ActualThemeChanged += (_, _) => ApplyVisuals();
    }

    private bool IsHighContrast()
    {
        try { return _accessibility.HighContrast; }
        catch { return false; }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBadge badge)
        {
            badge.ApplyVisuals();
        }
    }

    private void ApplyVisuals()
    {
        LabelText.Text = Text ?? string.Empty;
        StatusDot.Visibility = ShowDot ? Visibility.Visible : Visibility.Collapsed;

        (string bgKey, string fgKey, string dotKey) = Tone switch
        {
            StatusTone.Accent => ("StatusAccentBgBrush", "StatusAccentFgBrush", "StatusAccentDotBrush"),
            StatusTone.Success => ("StatusSuccessBgBrush", "StatusSuccessFgBrush", "StatusSuccessDotBrush"),
            StatusTone.Warning => ("StatusWarningBgBrush", "StatusWarningFgBrush", "StatusWarningDotBrush"),
            StatusTone.Danger => ("StatusDangerBgBrush", "StatusDangerFgBrush", "StatusDangerDotBrush"),
            _ => ("StatusNeutralBgBrush", "StatusNeutralFgBrush", "StatusNeutralDotBrush")
        };

        bool highContrast = IsHighContrast();
        string dictKey = highContrast
            ? "HighContrast"
            : (ActualTheme == ElementTheme.Light ? "Light" : "Default");

        PillBorder.Background = ResolveBrush(bgKey, dictKey);
        PillBorder.BorderThickness = highContrast ? new Thickness(1) : new Thickness(0);
        PillBorder.BorderBrush = highContrast ? ResolveBrush("CardBorderBrush", dictKey) : null;
        LabelText.Foreground = ResolveBrush(fgKey, dictKey);
        StatusDot.Fill = ResolveBrush(dotKey, dictKey);
    }

    private static Brush? ResolveBrush(string key, string dictKey)
    {
        if (Application.Current?.Resources is not ResourceDictionary root)
            return null;

        if (root.ThemeDictionaries.TryGetValue(dictKey, out object? dictObj)
            && dictObj is ResourceDictionary themeDict
            && themeDict.TryGetValue(key, out object? themedBrush)
            && themedBrush is Brush b)
        {
            return b;
        }

        return root.TryGetValue(key, out object? fallback) ? fallback as Brush : null;
    }
}
