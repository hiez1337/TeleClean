using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace TeleClean.App.Controls;

/// <summary>
/// Windows 11-style horizontal settings card that automatically stacks its action control on narrow widths.
/// </summary>
[ContentProperty(Name = nameof(ActionContent))]
public sealed partial class SettingsCard : UserControl
{
    private bool? _isStacked;

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingsCard),
            new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard),
            new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard),
            new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(SettingsCard),
            new PropertyMetadata(null, OnContentChanged));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public SettingsCard()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateContent();
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsCard card)
        {
            card.UpdateContent();
        }
    }

    private void UpdateContent()
    {
        IconGlyph.Glyph = Glyph ?? string.Empty;
        IconHost.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;
        HeaderText.Text = Header ?? string.Empty;
        DescriptionText.Text = Description ?? string.Empty;
        DescriptionText.Visibility = string.IsNullOrWhiteSpace(Description) ? Visibility.Collapsed : Visibility.Visible;
        ActionHost.Content = ActionContent;
    }

    private void OnCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0) return;
        bool stacked = e.NewSize.Width < 460;
        if (_isStacked == stacked) return;
        _isStacked = stacked;

        if (stacked)
        {
            ActionColumn.Width = new GridLength(0);
            Grid.SetRow(ActionHost, 1);
            Grid.SetColumn(ActionHost, 0);
            Grid.SetColumnSpan(ActionHost, 3);
            ActionHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            ActionColumn.Width = GridLength.Auto;
            Grid.SetRow(ActionHost, 0);
            Grid.SetColumn(ActionHost, 2);
            Grid.SetColumnSpan(ActionHost, 1);
            ActionHost.HorizontalAlignment = HorizontalAlignment.Right;
        }
    }
}
