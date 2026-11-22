using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using TeleClean.App.ViewModels;

namespace TeleClean.App.Views;

public sealed partial class ProgressView : Page
{
    public BulkExecutionViewModel ViewModel { get; }

    public ProgressView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<BulkExecutionViewModel>();
    }
}
