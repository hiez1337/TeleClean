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
            Header = "MTProxy URL",
            PlaceholderText = "tg://proxy?server=...&port=...&secret=...  или  https://t.me/proxy?...",
            Password = config.ProxyUrl ?? string.Empty,
            PasswordRevealMode = PasswordRevealMode.Peek,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var infoText = new TextBlock
        {
            Text = "Если Telegram заблокирован провайдером — вставьте ссылку MTProxy.\nОставьте поле пустым для прямого подключения.",
            FontSize = 12,
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
                ViewModel.CancelQrLogin();
                await App.Services.GetRequiredService<ITelegramService>().RecreateClientAsync();
                ViewModel.StartQrLogin();
            }
            catch
            {
                ViewModel.ErrorMessage = "Не удалось применить настройки подключения. Повторите попытку.";
            }
        }
    }
}
