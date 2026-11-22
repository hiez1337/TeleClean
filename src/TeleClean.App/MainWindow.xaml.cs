using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TeleClean.App.Messages;
using TeleClean.App.ViewModels;
using TeleClean.App.Views;
using TeleClean.Core.Interfaces;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;

namespace TeleClean.App;

public sealed partial class MainWindow : Window,
    IRecipient<AuthenticationStateChangedMessage>,
    IRecipient<StartBulkExecutionMessage>,
    IRecipient<ThemeChangedMessage>,
    IRecipient<BackdropChangedMessage>
{
    private AppWindow? _appWindow;

    public DialogListViewModel DialogListVm { get; }
    public BulkExecutionViewModel BulkExecutionVm { get; }

    public MainWindow()
    {
        DialogListVm = App.Services.GetRequiredService<DialogListViewModel>();
        BulkExecutionVm = App.Services.GetRequiredService<BulkExecutionViewModel>();

        InitializeComponent();
        ConfigureTitleBarAndWindow();

        // Lock window in Auth state on launch until authentication is confirmed
        ShowAuthOverlay(true);

        WeakReferenceMessenger.Default.Register<AuthenticationStateChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<StartBulkExecutionMessage>(this);
        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<BackdropChangedMessage>(this);

        LoadInitialPreferences();
        CheckInitialAuthState();
    }

    private async void LoadInitialPreferences()
    {
        try
        {
            var configService = App.Services.GetService<IConfigService>();
            if (configService != null)
            {
                var config = await configService.LoadConfigAsync();
                if (!string.IsNullOrWhiteSpace(config.Theme))
                {
                    SetAppTheme(config.Theme);
                }
                if (!string.IsNullOrWhiteSpace(config.Backdrop))
                {
                    SetBackdrop(config.Backdrop);
                }
            }
        }
        catch
        {
            // Default theme and Mica fallback
        }
    }

    private void ConfigureTitleBarAndWindow()
    {
        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(1100, 750));

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppDragArea);

            var titleBar = _appWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 128, 128, 128);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(45, 128, 128, 128);
        }
    }

    private async void CheckInitialAuthState()
    {
        var telegramService = App.Services.GetService<ITelegramService>();
        if (telegramService != null)
        {
            if (telegramService.IsAuthorized)
            {
                ShowAuthOverlay(false);
                SetActiveNavTab(0);
                NavigateTo(typeof(DialogsView));
                return;
            }

            try
            {
                bool hasSession = await telegramService.CheckExistingSessionAsync();
                if (hasSession)
                {
                    ShowAuthOverlay(false);
                    SetActiveNavTab(0);
                    NavigateTo(typeof(DialogsView));
                    return;
                }
            }
            catch
            {
                // Fall back to Auth screen on check fault
            }
        }

        ShowAuthOverlay(true);
    }

    public void ShowAuthOverlay(bool show)
    {
        AuthHostOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TopNavHeader.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetAppTheme(string theme)
    {
        if (RootGrid != null)
        {
            RootGrid.RequestedTheme = theme.ToLowerInvariant() switch
            {
                "light" => ElementTheme.Light,
                "dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    public void SetBackdrop(string backdropType)
    {
        SystemBackdrop = backdropType switch
        {
            "MicaAlt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "Acrylic" => new DesktopAcrylicBackdrop(),
            _ => new MicaBackdrop { Kind = MicaKind.Base }
        };
    }

    private void OnHeaderTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int tabIndex))
        {
            SetActiveNavTab(tabIndex);
            if (tabIndex == 4)
            {
                NavigateTo(typeof(ProgressView));
            }
            else
            {
                DialogListVm.SelectedCategoryIndex = tabIndex;
                NavigateTo(typeof(DialogsView));
            }
        }
    }

    private void OnSettingsButtonClicked(object sender, RoutedEventArgs e)
    {
        SetActiveNavTab(5);
        NavigateTo(typeof(SettingsView));
    }

    private void OnSearchAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ContentFrame.CurrentSourcePageType != typeof(DialogsView))
        {
            SetActiveNavTab(0);
            NavigateTo(typeof(DialogsView));
        }
        HeaderSearchBox.Focus(FocusState.Programmatic);
        HeaderSearchBox.SelectAll();
    }

    private void OnSearchBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CurrentSourcePageType != typeof(DialogsView))
        {
            SetActiveNavTab(0);
            NavigateTo(typeof(DialogsView));
        }
    }

    public void SetActiveNavTab(int tabIndex)
    {
        TabChannelsLine.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        TabChannelsText.FontWeight = tabIndex == 0 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        TabChannelsText.Foreground = (Brush)Application.Current.Resources[tabIndex == 0 ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];

        TabChatsLine.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        TabChatsText.FontWeight = tabIndex == 1 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        TabChatsText.Foreground = (Brush)Application.Current.Resources[tabIndex == 1 ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];

        TabBotsLine.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        TabBotsText.FontWeight = tabIndex == 2 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        TabBotsText.Foreground = (Brush)Application.Current.Resources[tabIndex == 2 ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];

        TabDeletedLine.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        TabDeletedText.FontWeight = tabIndex == 3 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        TabDeletedText.Foreground = (Brush)Application.Current.Resources[tabIndex == 3 ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];

        TabOperationsLine.Visibility = tabIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        TabOperationsText.FontWeight = tabIndex == 4 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        TabOperationsText.Foreground = (Brush)Application.Current.Resources[tabIndex == 4 ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];

        NavSettingsBtn.Background = tabIndex == 5 ? (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"] : new SolidColorBrush(Colors.Transparent);
        if (NavSettingsBtn.Content is FontIcon icon)
        {
            icon.Foreground = (Brush)Application.Current.Resources[tabIndex == 5 ? "AccentFillColorDefaultBrush" : "TextFillColorSecondaryBrush"];
        }
    }

    public void NavigateTo(Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    public void Receive(AuthenticationStateChangedMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ShowAuthOverlay(!message.Value);
            if (message.Value)
            {
                SetActiveNavTab(0);
                NavigateTo(typeof(DialogsView));
            }
            else
            {
                ContentFrame.Content = null;
            }
        });
    }

    public void Receive(StartBulkExecutionMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SetActiveNavTab(4);
            NavigateTo(typeof(ProgressView));
        });
    }

    public void Receive(ThemeChangedMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SetAppTheme(message.Value);
        });
    }

    public void Receive(BackdropChangedMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SetBackdrop(message.Value);
        });
    }
}
