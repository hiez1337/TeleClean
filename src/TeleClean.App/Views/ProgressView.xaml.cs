using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TeleClean.App.ViewModels;

namespace TeleClean.App.Views;

public sealed partial class ProgressView : Page
{
    public BulkExecutionViewModel ViewModel { get; }

    private void OnReturnToDialogs(object sender, RoutedEventArgs e) => App.MainWindow.ReturnToDialogs();

    public ProgressView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<BulkExecutionViewModel>();
    }
}
