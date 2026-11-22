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

        var proxyBox = new TextBox
        {
            Header = "MTProxy URL",
            PlaceholderText = "tg://proxy?server=...&port=...&secret=...  или  https://t.me/proxy?...",
            Text = config.ProxyUrl ?? string.Empty,
            IsSpellCheckEnabled = false,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var infoText = new TextBlock
        {
            Text = "Если Telegram заблокирован провайдером — вставьте ссылку MTProxy.\nОставьте поле пустым для прямого подключения.",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
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
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            config.ProxyUrl = string.IsNullOrWhiteSpace(proxyBox.Text) ? null : proxyBox.Text.Trim();
            await configService.SaveConfigAsync(config);

            // Dispose existing client so next auth call recreates it with the new proxy
            var telegramService = App.Services.GetRequiredService<ITelegramService>();
            await telegramService.RecreateClientAsync();

            // Restart QR login — new client will be created with updated proxy
            ViewModel.StartQrLogin();
        }
    }
}

