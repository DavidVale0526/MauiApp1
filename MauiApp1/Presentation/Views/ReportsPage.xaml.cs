using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class ReportsPage : CoordinatorRefreshPage
{
    private readonly ReportsViewModel _viewModel;

    public ReportsPage(ReportsViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.LoadAsync();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ReportsViewModel viewModel)
            await viewModel.LoadAsync();
    }

    private async void OnSalesClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is ReportsViewModel viewModel) await viewModel.ShowSalesAsync();
    }

    private async void OnInventoryClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is ReportsViewModel viewModel) await viewModel.ShowInventoryAsync();
    }

    private async void OnMovementsClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is ReportsViewModel viewModel) await viewModel.ShowMovementsAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is ReportsViewModel viewModel) await viewModel.RefreshAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        if (Navigation.ModalStack.Count > 0) await Navigation.PopModalAsync();
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }

    private void OnPageSizeChanged(object? sender, EventArgs eventArgs)
    {
        if (PageLayout.Width <= 0) return;
        var isDesktop = PageLayout.Width >= 900;
        PageLayout.Padding = isDesktop ? new Thickness(32, 28) : new Thickness(16, 20);
        ContentHost.WidthRequest = Math.Min(1260, Math.Max(0, PageLayout.Width - PageLayout.Padding.HorizontalThickness));
        ContentHost.RowSpacing = isDesktop ? 16 : 12;
        PageTitle.FontSize = isDesktop ? 30 : 26;

        ArrangeFilters(SalesFiltersGrid,
            [SalesFromField, SalesToField, SalesModeField, SalesCustomerField], isDesktop, true);
        ArrangeFilters(InventoryFiltersGrid,
            [InventoryCategoryField, InventoryStatusField], isDesktop, false);
        ArrangeFilters(MovementsFiltersGrid,
            [MovementsFromField, MovementsToField, MovementsProductField, MovementsTypeField], isDesktop, true);
    }

    private static void ArrangeFilters(Grid grid, View[] fields, bool isDesktop, bool hasDateRange)
    {
        var columns = isDesktop ? fields.Length : hasDateRange ? 2 : 1;
        grid.ColumnDefinitions.Clear();
        for (var index = 0; index < columns; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            if (isDesktop || (hasDateRange && index < 2))
            {
                Grid.SetRow(field, 0);
                Grid.SetColumn(field, index);
                Grid.SetColumnSpan(field, 1);
            }
            else
            {
                Grid.SetRow(field, hasDateRange ? index - 1 : index);
                Grid.SetColumn(field, 0);
                Grid.SetColumnSpan(field, columns);
            }
        }
    }
}
