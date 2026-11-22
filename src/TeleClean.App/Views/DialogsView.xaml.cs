using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TeleClean.App.ViewModels;

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
        if (ViewModel.VisibleDialogs.Count == 0)
        {
            await ViewModel.InitializeAsync();
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

        if (item != null && item.IsJoined)
        {
            item.IsSelected = !item.IsSelected;
        }
    }
}
