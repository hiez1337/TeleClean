using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TeleClean.App.ViewModels;
using TeleClean.Core.Models;

namespace TeleClean.App.Views;

public sealed partial class DialogsView : Page
{
    public DialogListViewModel ViewModel { get; }

    public DialogsView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<DialogListViewModel>();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasRequestedDialogs)
        {
            await ViewModel.InitializeAsync();
        }
    }

    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool wide = e.NewSize.Width >= 750;
        ComparisonHeading.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        NameHeading.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        SelectVisible.Content = wide ? null : "Выбрать видимые";
        Grid.SetColumnSpan(SelectVisible, wide ? 1 : 3);
        Grid.SetColumnSpan(SearchBox, wide ? 1 : 2);
        Grid.SetColumn(FilterBar, wide ? 1 : 0);
        Grid.SetColumnSpan(FilterBar, wide ? 1 : 2);
        Grid.SetRow(FilterBar, wide ? 0 : 1);
        FilterBar.Margin = wide ? new Thickness(0) : new Thickness(0, 6, 0, 0);
        ViewModel.ShowComparison = wide;
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
            ? "История будет удалена из вашего списка Telegram. Боты не будут заблокированы."
            : "Вы потеряете доступ к закрытым каналам и чатам без нового приглашения.";
        var preview = string.Join("\n", items.Take(4).Select(item => "• " + item.Title));
        if (items.Count > 4) preview += $"\nИ ещё {items.Count - 4}.";

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = delete ? $"Удалить диалоги ({items.Count})?" : $"Выйти из выбранных ({items.Count})?",
            Content = new TextBlock { Text = consequence + "\n\n" + preview, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = delete ? "Удалить" : "Выйти",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.ExecuteSelection(items);
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
