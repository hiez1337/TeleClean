using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TeleClean.App.ViewModels;
using TeleClean.Core.Models;
using Windows.UI.ViewManagement;

namespace TeleClean.App.Views;

public sealed partial class DialogsView : Page
{
    private readonly UISettings _uiSettings = new();

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
        var scale = _uiSettings.TextScaleFactor;
        bool wide = width >= 750 * scale;
        ComparisonHeading.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        NameHeading.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        SelectVisible.Content = wide ? null : "Выбрать видимые";
        Grid.SetColumnSpan(SelectVisible, wide ? 1 : 3);
        FilterColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(FilterBar, wide ? 2 : 0);
        Grid.SetColumnSpan(FilterBar, wide ? 1 : 2);
        Grid.SetRow(FilterBar, wide ? 0 : 1);
        FilterBar.Margin = wide ? new Thickness(0) : new Thickness(0, 6, 0, 0);
        ViewModel.ShowComparison = wide;

        bool stacked = width < 620 * scale;
        ClearColumn.Width = stacked ? new GridLength(0) : GridLength.Auto;
        ReviewColumn.Width = stacked ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Grid.SetColumnSpan(SelectionSummary, stacked ? 3 : 1);
        Grid.SetColumn(ClearSelection, stacked ? 0 : 1);
        Grid.SetRow(ClearSelection, stacked ? 1 : 0);
        Grid.SetRow(ReviewSelection, stacked ? 1 : 0);
        SelectionSummary.Margin = stacked ? new Thickness(0, 0, 0, 6) : new Thickness(0);
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
        var consequence = delete
            ? "История будет удалена из вашего списка Telegram. Бот останется доступен для новых сообщений."
            : "Чтобы вернуться в закрытый канал или чат, потребуется новое приглашение.";
        var preview = string.Join("\n", items.Take(4).Select(item => "•\u00a0" + item.Title));
        if (items.Count > 4) preview += $"\nЕщё {items.Count - 4} в выборе.";
        var review = new StackPanel { Spacing = 12 };
        review.Children.Add(new TextBlock { Text = consequence, TextWrapping = TextWrapping.Wrap });
        review.Children.Add(new ScrollViewer
        {
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = preview, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
        });

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

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.ExecuteSelection(items);
    }

    private async void OnShowDialogDetails(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: DialogItemViewModel item }) return;

        var details = new StackPanel { Spacing = 8 };
        details.Children.Add(new TextBlock
        {
            Text = item.Title, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            Style = (Style)Application.Current.Resources["SectionTitle"]
        });
        if (!string.IsNullOrEmpty(item.UsernameFormatted))
            details.Children.Add(new TextBlock
            {
                Text = item.UsernameFormatted, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
            });
        details.Children.Add(new TextBlock
        {
            Text = $"{item.CompactMetrics}\n{item.DisplayStatus}", TextWrapping = TextWrapping.Wrap
        });

        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "Сведения о диалоге",
            Content = new ScrollViewer
            {
                MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = details
            },
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Close
        }.ShowAsync();
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
