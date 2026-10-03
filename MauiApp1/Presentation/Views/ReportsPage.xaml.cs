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
}
