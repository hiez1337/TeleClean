using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TeleClean.App.ViewModels;
using TeleClean.Core.Interfaces;

namespace TeleClean.App.Views;

public sealed partial class AuthView : UserControl
{
    public AuthViewModel ViewModel { get; }

    public AuthView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<AuthViewModel>();
        Loaded += OnLoaded;
        ViewModel.PropertyChanged += (_, e) =>
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
        };
    }

    private void OnQrLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double qrSize = 260;
        bool stacked = e.NewSize.Width < 544;
        QrTile.Width = QrTile.Height = Math.Min(qrSize, e.NewSize.Width);
        QrTile.HorizontalAlignment = stacked ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        Grid.SetColumnSpan(QrTile, stacked ? 2 : 1);
        Grid.SetColumn(QrInstructions, stacked ? 0 : 1);
        Grid.SetColumnSpan(QrInstructions, stacked ? 2 : 1);
        Grid.SetRow(QrInstructions, stacked ? 1 : 0);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Opens a proxy-settings dialog before auth. Lets users set MTProxy URL
    /// without needing to be logged in (chicken-and-egg fix).
    /// </summary>
    private async void OnProxySettingsClick(object sender, RoutedEventArgs e)
    {
        var configService = App.Services.GetRequiredService<IConfigService>();
        var config = await configService.LoadConfigAsync();

        var proxyBox = new PasswordBox
        {
            Header = "Ссылка MTProxy",
            PlaceholderText = "tg://proxy или https://t.me/proxy",
            Password = config.ProxyUrl ?? string.Empty,
            PasswordRevealMode = PasswordRevealMode.Peek,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var infoText = new TextBlock
        {
            Text = "Вставьте ссылку MTProxy, если Telegram недоступен. Для прямого подключения оставьте поле пустым.",
            TextWrapping = TextWrapping.Wrap
        };

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(infoText);
        panel.Children.Add(proxyBox);

        var dialog = new ContentDialog
        {
            Title = "Настройки прокси",
            Content = panel,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                config.ProxyUrl = string.IsNullOrWhiteSpace(proxyBox.Password) ? null : proxyBox.Password.Trim();
                await configService.SaveConfigAsync(config);
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
