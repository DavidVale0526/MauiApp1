using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class PurchaseHistoryPage : CoordinatorRefreshPage
{
    private readonly PurchaseHistoryViewModel _viewModel;

    public PurchaseHistoryPage(PurchaseHistoryViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.LoadAsync();

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopModalAsync();

    private async void OnRefreshClicked(object? sender, EventArgs eventArgs) => await _viewModel.LoadAsync();

    private void OnPageSizeChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not Grid pageLayout)
        {
            return;
        }

        var isWide = Width >= 900;
        pageLayout.Padding = isWide ? new Thickness(32, 28) : new Thickness(16, 20);

        var stackStats = Width < 760;
        StatsGrid.ColumnDefinitions.Clear();
        StatsGrid.RowDefinitions.Clear();
        if (stackStats)
        {
            StatsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            StatsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            StatsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            StatsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        else
        {
            StatsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            StatsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            StatsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            StatsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        Grid.SetRow(StatPurchases, 0);
        Grid.SetColumn(StatPurchases, 0);
        Grid.SetRow(StatThisMonth, stackStats ? 1 : 0);
        Grid.SetColumn(StatThisMonth, stackStats ? 0 : 1);
        Grid.SetRow(StatUnits, stackStats ? 2 : 0);
        Grid.SetColumn(StatUnits, stackStats ? 0 : 2);
    }
}
