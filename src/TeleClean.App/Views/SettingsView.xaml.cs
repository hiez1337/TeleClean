using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TeleClean.App.Helpers;
using TeleClean.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace TeleClean.App.Views;

public sealed partial class SettingsView : Page
{
    private bool? _isTwoColumn;

    public SettingsViewModel ViewModel { get; }

    public SettingsView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Loaded += OnLoaded;
    }

    private void OnSettingsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0) return;
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        bool twoColumn = width >= 1120;
        if (_isTwoColumn == twoColumn) return;
        _isTwoColumn = twoColumn;

        if (twoColumn)
        {
            HeaderHost.Padding = new Thickness(28, 16, 28, 10);
            ContentHost.Padding = new Thickness(28, 4, 28, 28);
            HeaderWidthCol.MaxWidth = 1480;

            LeftColumn.MaxWidth = 730;
            RightColumn.Width = new GridLength(1, GridUnitType.Star);
            RightColumn.MaxWidth = 730;
            ContentHost.ColumnSpacing = 20;

            Grid.SetRow(RightColumnStack, 0);
            Grid.SetColumn(RightColumnStack, 1);
            AboutExpander.IsExpanded = true;
        }
        else
        {
            HeaderHost.Padding = new Thickness(20, 12, 20, 8);
            ContentHost.Padding = new Thickness(20, 2, 20, 24);
            HeaderWidthCol.MaxWidth = 900;

            LeftColumn.MaxWidth = 900;
            RightColumn.Width = new GridLength(0);
            RightColumn.MaxWidth = 0;
            ContentHost.ColumnSpacing = 0;

            Grid.SetRow(RightColumnStack, 1);
            Grid.SetColumn(RightColumnStack, 0);
            AboutExpander.IsExpanded = false;
        }
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
        ApplyResponsiveLayout(SettingsRoot.ActualWidth);
        await ViewModel.InitializeAsync();
    }
}
