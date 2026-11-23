using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TeleClean.App.ViewModels;

namespace TeleClean.App.Views;

public sealed partial class ProgressView : Page
{
    private bool? _isSplitLayout;

    public BulkExecutionViewModel ViewModel { get; }

    private void OnReturnToDialogs(object sender, RoutedEventArgs e) => App.MainWindow.ReturnToDialogs();

    public ProgressView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<BulkExecutionViewModel>();
        Loaded += (_, _) => ApplyResponsiveLayout(ProgressRoot.ActualWidth);
    }

    private void OnProgressSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0) return;
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        bool split = width >= 1120;
        if (_isSplitLayout == split) return;
        _isSplitLayout = split;

        if (split)
        {
            ProgressRoot.Padding = new Thickness(28, 16, 28, 20);
            SummaryColumn.Width = new GridLength(430);
            JournalColumn.Width = new GridLength(1, GridUnitType.Star);
            SummaryRow.Height = new GridLength(1, GridUnitType.Star);
            JournalRow.Height = new GridLength(0);
            DashboardGrid.ColumnSpacing = 18;
            DashboardGrid.RowSpacing = 0;

            SummaryCard.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(JournalCard, 0);
            Grid.SetColumn(JournalCard, 1);
        }
        else
        {
            ProgressRoot.Padding = new Thickness(20, 12, 20, 16);
            SummaryColumn.Width = new GridLength(1, GridUnitType.Star);
            JournalColumn.Width = new GridLength(0);
            SummaryRow.Height = GridLength.Auto;
            JournalRow.Height = new GridLength(1, GridUnitType.Star);
            DashboardGrid.ColumnSpacing = 0;
            DashboardGrid.RowSpacing = 12;

            SummaryCard.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetRow(JournalCard, 1);
            Grid.SetColumn(JournalCard, 0);
        }
    }
}
