using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class InventoryMovementHistoryPage : ContentPage
{
    private readonly InventoryMovementHistoryViewModel _viewModel;

    public InventoryMovementHistoryPage(InventoryMovementHistoryViewModel viewModel)
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

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopModalAsync();

    private async void OnRefreshClicked(object? sender, EventArgs eventArgs) => await _viewModel.LoadAsync();
}
