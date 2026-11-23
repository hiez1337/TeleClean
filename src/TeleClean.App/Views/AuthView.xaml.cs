using System;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TeleClean.App.Helpers;
using TeleClean.App.ViewModels;
using TeleClean.Core.Interfaces;
using Windows.System;

namespace TeleClean.App.Views;

public sealed partial class AuthView : UserControl
{
    private bool _initialized;
    private bool? _isQrStacked;

    public AuthViewModel ViewModel { get; }

    public AuthView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<AuthViewModel>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (!_initialized)
        {
            _initialized = true;
            await ViewModel.InitializeAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AuthViewModel.CurrentMode)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (ViewModel.CurrentMode)
            {
                case AuthMode.PhoneInput: PhoneInput.Focus(FocusState.Programmatic); break;
                case AuthMode.CodeInput: CodeInput.Focus(FocusState.Programmatic); break;
                case AuthMode.TwoFactor: PasswordInput.Focus(FocusState.Programmatic); break;
            }
        });
    }

    private void OnQrLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0) return;

        const double maxQrSize = 252;
        bool stacked = e.NewSize.Width < 530;
        double targetSize = stacked
            ? Math.Clamp(e.NewSize.Width, 180, maxQrSize)
            : maxQrSize;

        if (Math.Abs(QrTile.Width - targetSize) > 0.5)
        {
            QrTile.Width = QrTile.Height = targetSize;
        }

        if (_isQrStacked == stacked) return;
        _isQrStacked = stacked;

        QrTile.HorizontalAlignment = stacked ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        Grid.SetColumnSpan(QrTile, stacked ? 2 : 1);
        Grid.SetColumn(QrInstructions, stacked ? 0 : 1);
        Grid.SetColumnSpan(QrInstructions, stacked ? 2 : 1);
        Grid.SetRow(QrInstructions, stacked ? 1 : 0);
    }

    private async void OnPhoneInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.IsSendCodeButtonVisible && ViewModel.IsReady)
        {
            e.Handled = true;
            await ViewModel.SendPhoneCodeAsync();
        }
    }

    private async void OnCodeInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.IsCodeInputVisible && ViewModel.IsReady)
        {
            e.Handled = true;
            await ViewModel.SubmitPhoneCodeAsync();
        }
    }

    private async void OnPasswordInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.Is2FaMode && ViewModel.IsReady)
        {
            e.Handled = true;
            await ViewModel.Submit2FaAsync();
        }
    }

    /// <summary>
    /// Opens a proxy-settings dialog before auth so users can configure MTProxy before logging in.
    /// </summary>
    private async void OnProxySettingsClick(object sender, RoutedEventArgs e)
    {
        var configService = App.Services.GetRequiredService<IConfigService>();
        var config = await configService.LoadConfigAsync();

        var proxyBox = new PasswordBox
        {
            Header = "Ссылка MTProxy",
            PlaceholderText = "tg://proxy?server=...&port=...&secret=...",
            Password = config.ProxyUrl ?? string.Empty,
            PasswordRevealMode = PasswordRevealMode.Peek,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var infoText = new TextBlock
        {
            Text = "Вставьте ссылку MTProxy, если Telegram недоступен напрямую. Чтобы отключить прокси, оставьте поле пустым.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SupportingTextStyle"]
        };

        var validationBar = new InfoBar
        {
            Severity = InfoBarSeverity.Error,
            IsClosable = false,
            IsOpen = false
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(infoText);
        panel.Children.Add(proxyBox);
        panel.Children.Add(validationBar);

        string? validatedProxy = null;
        var dialog = new ContentDialog
        {
            Title = "Настройки подключения",
            Content = panel,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme,
            XamlRoot = XamlRoot
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!ProxyUrlValidator.TryValidateAndNormalize(proxyBox.Password, out string? normalized, out string? err))
            {
                validationBar.Message = err;
                validationBar.IsOpen = true;
                args.Cancel = true;
                return;
            }

            validationBar.IsOpen = false;
            validatedProxy = normalized;
        };

        var result = await DialogHostHelper.ShowSafeAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                config.ProxyUrl = validatedProxy;
                await configService.SaveConfigAsync(config);
                ViewModel.RefreshProxyIndicator();

                bool restoringSession = ViewModel.IsRecoveryMode;
                ViewModel.CancelQrLogin();
                await App.Services.GetRequiredService<ITelegramService>().RecreateClientAsync();
                if (restoringSession) await ViewModel.InitializeAsync();
                else ViewModel.StartQrLogin();
            }
            catch
            {
                ViewModel.ErrorMessage = "Не удалось применить настройки подключения. Повторите попытку.";
            }
        }
    }
}
