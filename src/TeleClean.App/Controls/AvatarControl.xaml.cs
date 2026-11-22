using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace TeleClean.App.Controls;

public sealed partial class AvatarControl : UserControl
{
    private static readonly Color[] TelegramPalette =
    [
        Color.FromArgb(255, 231, 76, 60),   // Red
        Color.FromArgb(255, 230, 126, 34),  // Orange
        Color.FromArgb(255, 155, 89, 182),  // Violet
        Color.FromArgb(255, 46, 204, 113),  // Green
        Color.FromArgb(255, 26, 188, 156),  // Cyan
        Color.FromArgb(255, 41, 144, 255),  // Blue
        Color.FromArgb(255, 233, 30, 99)    // Pink
    ];

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AvatarControl), new PropertyMetadata(string.Empty, OnDataChanged));

    public static readonly DependencyProperty AvatarPathProperty =
        DependencyProperty.Register(nameof(AvatarPath), typeof(string), typeof(AvatarControl), new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty IsDimmedProperty =
        DependencyProperty.Register(nameof(IsDimmed), typeof(bool), typeof(AvatarControl), new PropertyMetadata(false, OnDimmedChanged));

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
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AvatarControl control)
        {
            control.UpdateVisuals();
        }
    }

    private static void OnDimmedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AvatarControl control)
        {
            control.RootContainer.Opacity = control.IsDimmed ? 0.4 : 1.0;
        }
    }

    private void UpdateVisuals()
    {
        // 1. Initial calculation and color selection
        string title = Title ?? string.Empty;
        string initial = !string.IsNullOrWhiteSpace(title) ? title.TrimStart()[0].ToString().ToUpperInvariant() : "?";
        InitialsText.Text = initial;

        uint hash = (uint)Math.Abs(title.GetHashCode());
        Color circleColor = TelegramPalette[hash % (uint)TelegramPalette.Length];
        FallbackCircle.Fill = new SolidColorBrush(circleColor);

        // 2. Corrupted file guard and disk image loading
        if (!string.IsNullOrWhiteSpace(AvatarPath) && File.Exists(AvatarPath))
        {
            try
            {
                var fileInfo = new FileInfo(AvatarPath);
                if (fileInfo.Length > 0)
                {
                    var bitmap = new BitmapImage(new Uri(AvatarPath));
                    AvatarImageBrush.ImageSource = bitmap;
                    ImageCircle.Visibility = Visibility.Visible;
                    FallbackCircle.Visibility = Visibility.Collapsed;
                    InitialsText.Visibility = Visibility.Collapsed;
                    return;
                }
            }
            catch
            {
                // Fall back gracefully to initials on file corruption or parse fault
            }
        }

        ImageCircle.Visibility = Visibility.Collapsed;
        FallbackCircle.Visibility = Visibility.Visible;
        InitialsText.Visibility = Visibility.Visible;
    }
}
