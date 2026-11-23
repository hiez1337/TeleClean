using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TeleClean.App.Helpers;
using TeleClean.App.ViewModels;
using TeleClean.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.ViewManagement;

namespace TeleClean.App.Views;

public sealed partial class DialogsView : Page
{
    private readonly UISettings _uiSettings = new();
    private bool? _isWide;
    private bool? _isFullscreen;
    private bool? _isFooterStacked;
    private bool? _isCompactRefresh;

    public DialogListViewModel ViewModel { get; }

    public DialogsView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<DialogListViewModel>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _uiSettings.TextScaleFactorChanged -= OnTextScaleFactorChanged;
        _uiSettings.TextScaleFactorChanged += OnTextScaleFactorChanged;
        ApplyLayout(Workspace.ActualWidth);
        if (!ViewModel.HasRequestedDialogs)
        {
            await ViewModel.InitializeAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        _uiSettings.TextScaleFactorChanged -= OnTextScaleFactorChanged;

    private void OnTextScaleFactorChanged(UISettings sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsLoaded) ApplyLayout(Workspace.ActualWidth);
        });

    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize.Width);

    private void ApplyLayout(double width)
    {
        if (width <= 0) return;

        double scale = _uiSettings.TextScaleFactor;
        bool wide = width >= 740 * scale;
        bool fullscreen = width >= 1220 * scale;

        if (_isWide != wide || _isFullscreen != fullscreen)
        {
            _isWide = wide;
            _isFullscreen = fullscreen;

            Workspace.Padding = fullscreen ? new Thickness(28, 16, 28, 18) : new Thickness(20, 12, 20, 14);
            ContentHost.RowSpacing = fullscreen ? 12 : 10;

            ComparisonHeading.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            NameHeading.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            SelectVisible.Content = wide ? null : "Выбрать видимые";
            SelectVisible.Padding = wide ? new Thickness(0) : new Thickness(8, 0, 0, 0);
            Grid.SetColumnSpan(SelectVisible, wide ? 1 : 3);

            FilterColumn.Width = wide ? GridLength.Auto : new GridLength(0);
            Grid.SetColumn(FilterBar, wide ? 1 : 0);
            Grid.SetColumnSpan(FilterBar, wide ? 1 : 2);
            Grid.SetRow(FilterBar, wide ? 0 : 1);

            FilterPickerColumn.Width = wide ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            SortPickerColumn.Width = wide ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            FilterPicker.MinWidth = fullscreen ? 176 : (wide ? 144 : 0);
            SortPicker.MinWidth = fullscreen ? 196 : (wide ? 164 : 0);

            ViewModel.ShowComparison = wide;
        }

        bool compactRefresh = width < 520 * scale;
        if (_isCompactRefresh != compactRefresh)
        {
            _isCompactRefresh = compactRefresh;
            RefreshLabel.Visibility = compactRefresh ? Visibility.Collapsed : Visibility.Visible;
        }

        bool footerStacked = width < 560 * scale;
        if (_isFooterStacked != footerStacked)
        {
            _isFooterStacked = footerStacked;
            FooterActionsColumn.Width = footerStacked ? new GridLength(0) : GridLength.Auto;
            Grid.SetColumnSpan(SelectionSummaryPanel, footerStacked ? 2 : 1);
            Grid.SetColumn(FooterActionsPanel, footerStacked ? 0 : 1);
            Grid.SetColumnSpan(FooterActionsPanel, footerStacked ? 2 : 1);
            Grid.SetRow(FooterActionsPanel, footerStacked ? 1 : 0);
            FooterActionsPanel.HorizontalAlignment = footerStacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            FooterStatusText.Visibility = footerStacked ? Visibility.Collapsed : (ViewModel.HasStatusMessage ? Visibility.Visible : Visibility.Collapsed);
        }
    }

    private void OnSearchShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = SearchBox.Focus(FocusState.Keyboard);
        if (args.Handled) SearchBox.SelectAll();
    }

    private void OnSelectVisible(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsAllSelectedInView == true) ViewModel.DeselectAll();
        else ViewModel.SelectAll();
    }

    private async void OnReviewSelection(object sender, RoutedEventArgs e)
    {
        var items = ViewModel.GetSelectedItems();
        if (items.Count == 0) return;

        bool delete = items[0].DialogType is DialogType.Bot or DialogType.Deleted or DialogType.User;
        string consequence = delete
            ? "История будет удалена из вашего списка Telegram. Бот останется доступен для новых сообщений."
            : "Чтобы вернуться в закрытый канал или чат, потребуется новое приглашение.";

        var warningBanner = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsClosable = false,
            IsOpen = true,
            Message = consequence
        };

        var preview = string.Join("\n", items.Take(6).Select(item => "•\u00a0" + item.Title));
        if (items.Count > 6) preview += $"\n… и ещё {items.Count - 6} в выборе.";

        var listBox = new Border
        {
            Style = (Style)Application.Current.Resources["SubtleTileStyle"],
            Child = new ScrollViewer
            {
                MaxHeight = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = preview,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    LineHeight = 22
                }
            }
        };

        var review = new StackPanel { Spacing = 12 };
        review.Children.Add(warningBanner);
        review.Children.Add(listBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = delete ? $"Удалить диалоги ({items.Count})?" : $"Выйти из выбранных ({items.Count})?",
            Content = review,
            PrimaryButtonText = delete ? "Удалить" : "Выйти",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };

        if (await DialogHostHelper.ShowSafeAsync(dialog) == ContentDialogResult.Primary)
            ViewModel.ExecuteSelection(items);
    }

    private async void OnShowDialogDetails(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: DialogItemViewModel item }) return;

        var details = new StackPanel { Spacing = 10 };
        details.Children.Add(new TextBlock
        {
            Text = item.Title,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Style = (Style)Application.Current.Resources["SectionTitleStyle"]
        });

        if (!string.IsNullOrEmpty(item.UsernameFormatted))
        {
            details.Children.Add(new TextBlock
            {
                Text = item.UsernameFormatted,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Style = (Style)Application.Current.Resources["SupportingTextStyle"]
            });
        }

        details.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["SubtleTileStyle"],
            Child = new TextBlock
            {
                Text = $"{item.CompactMetrics}\nСтатус: {item.CleanStatusText}",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                LineHeight = 22
            }
        });

        await DialogHostHelper.ShowSafeAsync(new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "Сведения о диалоге",
            Content = new ScrollViewer
            {
                MaxHeight = 280,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = details
            },
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Close
        });
    }

    private void OnCopyDialogTitle(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: DialogItemViewModel item }) return;
        try
        {
            var data = new DataPackage();
            string text = string.IsNullOrEmpty(item.UsernameFormatted)
                ? item.Title
                : $"{item.Title} ({item.UsernameFormatted})";
            data.SetText(text);
            Clipboard.SetContent(data);
            ViewModel.StatusMessage = "Название скопировано";
        }
        catch
        {
            // Ignore clipboard errors in restricted sessions.
        }
    }

    private void OnRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            DependencyObject? current = dep;
            while (current != null && !ReferenceEquals(current, sender))
            {
                if (current is ButtonBase)
                {
                    return;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }

        var item = (sender as FrameworkElement)?.DataContext as DialogItemViewModel
                   ?? (sender as FrameworkElement)?.Tag as DialogItemViewModel;

        if (item != null && item.CanToggle)
        {
            item.IsSelected = !item.IsSelected;
        }
    }
}
