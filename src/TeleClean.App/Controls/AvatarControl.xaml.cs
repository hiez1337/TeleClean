using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TeleClean.App.Helpers;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TeleClean.App.Controls;

public sealed partial class AvatarControl : UserControl
{
    private static readonly (Color Bg, Color Fg)[] DarkPalette =
    {
        (Color.FromArgb(255, 36, 59, 92),  Color.FromArgb(255, 134, 203, 255)), // Azure
        (Color.FromArgb(255, 29, 66, 68),  Color.FromArgb(255, 114, 224, 214)), // Teal
        (Color.FromArgb(255, 50, 47, 88),  Color.FromArgb(255, 179, 168, 255)), // Indigo
        (Color.FromArgb(255, 74, 42, 74),  Color.FromArgb(255, 240, 162, 232)), // Plum
        (Color.FromArgb(255, 82, 46, 54),  Color.FromArgb(255, 255, 163, 178)), // Rose
        (Color.FromArgb(255, 76, 60, 32),  Color.FromArgb(255, 252, 208, 120)), // Amber
        (Color.FromArgb(255, 32, 68, 52),  Color.FromArgb(255, 120, 227, 172)), // Emerald
    };

    private static readonly (Color Bg, Color Fg)[] LightPalette =
    {
        (Color.FromArgb(255, 219, 236, 254), Color.FromArgb(255, 18, 92, 168)),  // Azure
        (Color.FromArgb(255, 209, 244, 240), Color.FromArgb(255, 15, 110, 102)), // Teal
        (Color.FromArgb(255, 227, 229, 254), Color.FromArgb(255, 67, 56, 176)),  // Indigo
        (Color.FromArgb(255, 245, 224, 247), Color.FromArgb(255, 134, 38, 140)), // Plum
        (Color.FromArgb(255, 252, 226, 232), Color.FromArgb(255, 174, 30, 68)),  // Rose
        (Color.FromArgb(255, 254, 240, 204), Color.FromArgb(255, 154, 88, 10)),  // Amber
        (Color.FromArgb(255, 212, 245, 226), Color.FromArgb(255, 16, 116, 68)),  // Emerald
    };

    private readonly AccessibilitySettings _accessibility = new();
    private string? _loadedPath;

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AvatarControl),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty AvatarPathProperty =
        DependencyProperty.Register(nameof(AvatarPath), typeof(string), typeof(AvatarControl),
            new PropertyMetadata(null, OnAvatarPathChanged));

    public static readonly DependencyProperty IsDimmedProperty =
        DependencyProperty.Register(nameof(IsDimmed), typeof(bool), typeof(AvatarControl),
            new PropertyMetadata(false, OnDimmedChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? AvatarPath
    {
        get => (string?)GetValue(AvatarPathProperty);
        set => SetValue(AvatarPathProperty, value);
    }

    public bool IsDimmed
    {
        get => (bool)GetValue(IsDimmedProperty);
        set => SetValue(IsDimmedProperty, value);
    }

    public AvatarControl()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyInitialsAndPalette();
        ActualThemeChanged += (_, _) => ApplyInitialsAndPalette();
    }

    private bool IsHighContrast()
    {
        try { return _accessibility.HighContrast; }
        catch { return false; }
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AvatarControl control)
        {
            control.ApplyInitialsAndPalette();
        }
    }

    private static void OnAvatarPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AvatarControl control)
        {
            control.ApplyImage();
        }
    }

    private static void OnDimmedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AvatarControl control)
        {
            control.RootContainer.Opacity = control.IsDimmed ? 0.5 : 1.0;
        }
    }

    private void ApplyInitialsAndPalette()
    {
        string title = Title ?? string.Empty;
        string initials = AvatarInitialsHelper.GetInitials(title);
        InitialsText.Text = initials;
        InitialsText.FontSize = initials.Length > 1 ? 12 : 14;

        if (IsHighContrast() && Application.Current?.Resources is ResourceDictionary resources
            && resources.ThemeDictionaries.TryGetValue("HighContrast", out object? hcObj)
            && hcObj is ResourceDictionary hc)
        {
            FallbackCircle.Fill = hc["StatusNeutralBgBrush"] as Brush;
            FallbackCircle.Stroke = hc["CardBorderBrush"] as Brush;
            FallbackCircle.StrokeThickness = 1;
            InitialsText.Foreground = hc["StatusNeutralFgBrush"] as Brush;
            return;
        }

        int index = AvatarInitialsHelper.GetPaletteIndex(title);
        var palette = ActualTheme == ElementTheme.Light ? LightPalette : DarkPalette;
        var (bg, fg) = palette[index];
        FallbackCircle.Fill = new SolidColorBrush(bg);
        FallbackCircle.Stroke = null;
        FallbackCircle.StrokeThickness = 0;
        InitialsText.Foreground = new SolidColorBrush(fg);
    }

    private void ApplyImage()
    {
        string? path = AvatarPath;
        if (string.Equals(_loadedPath, path, StringComparison.Ordinal))
        {
            return;
        }

        _loadedPath = path;

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage { DecodePixelWidth = 96 };
                bitmap.ImageFailed += (_, _) =>
                {
                    if (string.Equals(_loadedPath, path, StringComparison.Ordinal))
                    {
                        ShowFallback();
                    }
                };
                bitmap.UriSource = new Uri(path);
                AvatarImageBrush.ImageSource = bitmap;
                ImageCircle.Visibility = Visibility.Visible;
                FallbackCircle.Visibility = Visibility.Collapsed;
                InitialsText.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                // Fall back gracefully to initials on parse/URI fault.
            }
        }

        ShowFallback();
    }

    private void ShowFallback()
    {
        AvatarImageBrush.ImageSource = null;
        ImageCircle.Visibility = Visibility.Collapsed;
        FallbackCircle.Visibility = Visibility.Visible;
        InitialsText.Visibility = Visibility.Visible;
    }
}
