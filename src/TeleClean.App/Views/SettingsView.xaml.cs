using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TeleClean.App.ViewModels;

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
            Content = new TextBlock { Text = "Приложение завершит эту сессию Telegram. Для продолжения потребуется войти снова.", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Выйти",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() == ContentDialogResult.Primary && list.CanRefresh)
        {
            list.SetOperationActive(true);
            try { await ViewModel.ResetSessionAsync(); }
            finally { list.SetOperationActive(false); }
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }
}
