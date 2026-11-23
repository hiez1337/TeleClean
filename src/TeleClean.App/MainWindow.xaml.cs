using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TeleClean.App.Helpers;
using TeleClean.App.Messages;
using TeleClean.App.ViewModels;
using TeleClean.App.Views;
using TeleClean.Core.Interfaces;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace TeleClean.App;

public sealed partial class MainWindow : Window,
    IRecipient<AuthenticationStateChangedMessage>, IRecipient<StartBulkExecutionMessage>,
    IRecipient<ThemeChangedMessage>, IRecipient<BackdropChangedMessage>
{
    private readonly AccessibilitySettings _accessibility = new();
    private readonly WindowBackdropManager _backdropManager;
    private object? _lastDialogCategory;
    private bool _closing;
    private bool _shutdownComplete;

    private bool IsHighContrast()
    {
        try { return _accessibility.HighContrast; }
        catch { return false; }
    }

    public void ReturnToDialogs() => Navigation.SelectedItem = _lastDialogCategory ?? Channels;

    public DialogListViewModel DialogListVm { get; }
    public BulkExecutionViewModel BulkExecutionVm { get; }

    public MainWindow(bool isDemoMode = false)
    {
        DialogListVm = App.Services.GetRequiredService<DialogListViewModel>();
        BulkExecutionVm = App.Services.GetRequiredService<BulkExecutionViewModel>();
        BulkExecutionVm.PropertyChanged += OnBulkExecutionPropertyChanged;

        InitializeComponent();
        _backdropManager = new WindowBackdropManager(this, RootGrid);
        Activated += OnWindowActivated;

        if (isDemoMode)
        {
            DemoBadge.Visibility = Visibility.Visible;
        }

        ConfigureTitleBar();

        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min(workArea.Width, (int)(1100 * scale)),
            Math.Min(workArea.Height, (int)(750 * scale))));

        AppWindow.Closing += OnWindowClosing;
        Closed += OnWindowClosed;

        WeakReferenceMessenger.Default.RegisterAll(this);
        var config = App.Services.GetRequiredService<IConfigService>().CurrentConfig;
        RootGrid.ActualThemeChanged += OnActualThemeChanged;

        SetAppTheme(config.Theme);
        SetBackdrop(config.Backdrop);
    }

    private void ConfigureTitleBar()
    {
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            SetTitleBar(AppTitleBar);
        }
    }

    private void OnBulkExecutionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BulkExecutionViewModel.IsOperationActive))
        {
            bool active = BulkExecutionVm.IsOperationActive;
            DispatcherQueue.TryEnqueue(() => DialogListVm.SetOperationActive(active));
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _backdropManager.UpdateActivation(args.WindowActivationState != WindowActivationState.Deactivated);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateTitleBarTheme();
        _backdropManager.UpdateTheme();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnWindowActivated;
        BulkExecutionVm.PropertyChanged -= OnBulkExecutionPropertyChanged;
        RootGrid.ActualThemeChanged -= OnActualThemeChanged;
        _backdropManager.Dispose();
        WeakReferenceMessenger.Default.UnregisterAll(this);
        AuthViewControl.ViewModel.CancelQrLogin();
    }

    private async void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownComplete) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        RootGrid.IsHitTestVisible = false;
        AuthViewControl.ViewModel.CancelQrLogin();
        BulkExecutionVm.Stop();
        DialogListVm.Dispose();

        try
        {
            if (App.Services is IAsyncDisposable provider) await provider.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TeleClean shutdown: {ex.GetType().Name}");
        }
        finally
        {
            _shutdownComplete = true;
            Close();
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, int size);

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
        bool isDark = RootGrid.ActualTheme == ElementTheme.Dark;
        int darkFlag = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), 20, ref darkFlag, sizeof(int));

        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        var titleBar = AppWindow.TitleBar;
        if (IsHighContrast())
        {
            titleBar.ButtonBackgroundColor = null;
            titleBar.ButtonInactiveBackgroundColor = null;
            titleBar.ButtonForegroundColor = null;
            titleBar.ButtonHoverForegroundColor = null;
            titleBar.ButtonHoverBackgroundColor = null;
            titleBar.ButtonPressedForegroundColor = null;
            titleBar.ButtonPressedBackgroundColor = null;
            titleBar.ButtonInactiveForegroundColor = null;
            return;
        }

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        if (isDark)
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 240, 244, 248);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(26, 255, 255, 255);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(220, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(16, 255, 255, 255);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(130, 255, 255, 255);
        }
        else
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 28, 32, 38);
            titleBar.ButtonHoverForegroundColor = Colors.Black;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(18, 0, 0, 0);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(200, 0, 0, 0);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(10, 0, 0, 0);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(115, 0, 0, 0);
        }
    }

    private void OnPaneToggleClick(object sender, RoutedEventArgs e)
    {
        Navigation.IsPaneOpen = !Navigation.IsPaneOpen;
    }

    private void OnNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        sender.IsPaneOpen = args.DisplayMode == NavigationViewDisplayMode.Expanded;
    }

    private async void OnRefreshShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Navigation.Visibility != Visibility.Visible || ContentFrame.Content is not DialogsView) return;
        args.Handled = true;
        await DialogListVm.LoadDialogsAsync();
    }

    private bool? _isUltraWidePane;

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0) return;
        bool ultraWide = e.NewSize.Width >= 1400;
        if (_isUltraWidePane == ultraWide) return;
        _isUltraWidePane = ultraWide;
        Navigation.OpenPaneLength = ultraWide ? 240 : 216;
    }

    public void SetBackdrop(string backdropType)
    {
        bool translucentActive = _backdropManager.Apply(backdropType);
        SolidBackdropLayer.Visibility = translucentActive ? Visibility.Collapsed : Visibility.Visible;
    }

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
            bool authenticated = message.Value;
            AuthViewControl.Visibility = authenticated ? Visibility.Collapsed : Visibility.Visible;
            Navigation.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
            PaneToggleButton.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;

            if (authenticated)
            {
                Navigation.SelectedItem = Channels;
            }
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

    public void Receive(ThemeChangedMessage message) =>
        DispatcherQueue.TryEnqueue(() => SetAppTheme(message.Value));

    public void Receive(BackdropChangedMessage message) =>
        DispatcherQueue.TryEnqueue(() => SetBackdrop(message.Value));
}
