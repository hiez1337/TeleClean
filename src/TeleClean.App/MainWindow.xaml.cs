using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using TeleClean.App.Messages;
using TeleClean.App.ViewModels;
using TeleClean.App.Views;
using TeleClean.Core.Interfaces;

namespace TeleClean.App;

public sealed partial class MainWindow : Window,
    IRecipient<AuthenticationStateChangedMessage>, IRecipient<StartBulkExecutionMessage>,
    IRecipient<ThemeChangedMessage>, IRecipient<BackdropChangedMessage>
{
    private object? _lastDialogCategory;

    public void ReturnToDialogs() => Navigation.SelectedItem = _lastDialogCategory ?? Channels;

    public DialogListViewModel DialogListVm { get; }
    public BulkExecutionViewModel BulkExecutionVm { get; }

    public MainWindow()
    {
        DialogListVm = App.Services.GetRequiredService<DialogListViewModel>();
        BulkExecutionVm = App.Services.GetRequiredService<BulkExecutionViewModel>();
        BulkExecutionVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(BulkExecutionViewModel.IsOperationActive))
                DialogListVm.SetOperationActive(BulkExecutionVm.IsOperationActive);
        };
        InitializeComponent();
        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min(workArea.Width, (int)(1100 * scale)),
            Math.Min(workArea.Height, (int)(750 * scale))));
        Closed += (_, _) =>
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            AuthViewControl.ViewModel.CancelQrLogin();
        };

        WeakReferenceMessenger.Default.RegisterAll(this);
        var config = App.Services.GetRequiredService<IConfigService>().CurrentConfig;
        RootGrid.ActualThemeChanged += (_, _) => UpdateTitleBarTheme();
        SetAppTheme(config.Theme);
        SetBackdrop(config.Backdrop);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    public void SetAppTheme(string theme)
    {
        RootGrid.RequestedTheme = theme?.ToLowerInvariant() switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        UpdateTitleBarTheme();
    }

    private void UpdateTitleBarTheme()
    {
        // Preserve the system caption buttons and window behavior.
        int dark = RootGrid.ActualTheme == ElementTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, int size);

    private void OnNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        sender.IsPaneOpen = args.DisplayMode == NavigationViewDisplayMode.Expanded;
        if (ContentFrame != null)
            ContentFrame.Margin = args.DisplayMode == NavigationViewDisplayMode.Minimal
                ? new Thickness(0, 48, 0, 0) : new Thickness(0);
    }

    private async void OnRefreshShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Navigation.Visibility != Visibility.Visible || ContentFrame.Content is not DialogsView) return;
        args.Handled = true;
        await DialogListVm.LoadDialogsAsync();
    }

    public void SetBackdrop(string backdropType) => SystemBackdrop = backdropType switch
    {
        "MicaAlt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
        "Acrylic" => new DesktopAcrylicBackdrop(),
        _ => new MicaBackdrop { Kind = MicaKind.Base }
    };

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || !int.TryParse(item.Tag?.ToString(), out int index))
            return;

        Type page;
        if (index < 4)
        {
            _lastDialogCategory = item;
            DialogListVm.SelectedCategoryIndex = index;
            page = typeof(DialogsView);
        }
        else page = index == 4 ? typeof(ProgressView) : typeof(SettingsView);

        if (ContentFrame.Content?.GetType() != page) ContentFrame.Navigate(page);
        if (sender.DisplayMode != NavigationViewDisplayMode.Expanded) sender.IsPaneOpen = false;
    }

    public void Receive(AuthenticationStateChangedMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AuthViewControl.Visibility = message.Value ? Visibility.Collapsed : Visibility.Visible;
            Navigation.Visibility = message.Value ? Visibility.Visible : Visibility.Collapsed;
            if (message.Value) Navigation.SelectedItem = Channels;
            else
            {
                Navigation.SelectedItem = null;
                ContentFrame.Content = null;
                DialogListVm.ResetAccount();
            }
        });
    }

    public void Receive(StartBulkExecutionMessage message) =>
        DispatcherQueue.TryEnqueue(() => Navigation.SelectedItem = Operations);

    public void Receive(ThemeChangedMessage message) => SetAppTheme(message.Value);
    public void Receive(BackdropChangedMessage message) => SetBackdrop(message.Value);
}
