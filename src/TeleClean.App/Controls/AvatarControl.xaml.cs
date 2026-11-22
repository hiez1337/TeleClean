using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TeleClean.App.Controls;

public sealed partial class AvatarControl : UserControl
{
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
        // Keep the initials visible until an image is available.
        string title = Title ?? string.Empty;
        string initial = !string.IsNullOrWhiteSpace(title) ? System.Globalization.StringInfo.GetNextTextElement(title.TrimStart()).ToUpperInvariant() : "?";
        InitialsText.Text = initial;

        // 2. Corrupted file guard and disk image loading
        if (!string.IsNullOrWhiteSpace(AvatarPath) && File.Exists(AvatarPath))
        {
            try
            {
                var fileInfo = new FileInfo(AvatarPath);
                if (fileInfo.Length > 0)
                {
                    var bitmap = new BitmapImage(new Uri(AvatarPath)) { DecodePixelWidth = 96 };
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

        AvatarImageBrush.ImageSource = null;
        ImageCircle.Visibility = Visibility.Collapsed;
        FallbackCircle.Visibility = Visibility.Visible;
        InitialsText.Visibility = Visibility.Visible;
    }
}
