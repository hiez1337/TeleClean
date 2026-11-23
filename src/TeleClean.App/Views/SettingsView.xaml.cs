using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TeleClean.App.Helpers;
using TeleClean.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace TeleClean.App.Views;

public sealed partial class SettingsView : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Loaded += OnLoaded;
    }

    private async void OnConfirmLogout(object sender, RoutedEventArgs e)
    {
        var list = App.Services.GetRequiredService<DialogListViewModel>();
        if (!list.CanRefresh)
        {
            ViewModel.ErrorMessage = "Дождитесь окончания загрузки или остановите текущую операцию.";
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "Выйти из Telegram в TeleClean?",
            Content = new TextBlock
            {
                Text = "Приложение завершит текущую сессию Telegram на этом устройстве. Для продолжения потребуется войти снова.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Выйти",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };

        if (await DialogHostHelper.ShowSafeAsync(confirmation) == ContentDialogResult.Primary && list.CanRefresh)
        {
            list.SetOperationActive(true);
            try { await ViewModel.ResetSessionAsync(); }
            finally { list.SetOperationActive(false); }
        }
    }

    private void OnCopyDataDirectory(object sender, RoutedEventArgs e)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(ViewModel.DataDirectory);
            Clipboard.SetContent(data);
            ViewModel.StatusMessage = "Путь к папке данных скопирован.";
            ViewModel.ErrorMessage = null;
        }
        catch
        {
            ViewModel.ErrorMessage = "Не удалось скопировать путь в буфер обмена.";
        }
    }

    private void OnErrorBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason == InfoBarCloseReason.CloseButton)
        {
            ViewModel.ErrorMessage = null;
        }
    }

    private void OnStatusBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (args.Reason == InfoBarCloseReason.CloseButton)
        {
            ViewModel.StatusMessage = null;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }
}
