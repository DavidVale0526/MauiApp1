using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class PurchaseHistoryPage : ContentPage
{
    private readonly PurchaseHistoryViewModel _viewModel;

    public PurchaseHistoryPage(PurchaseHistoryViewModel viewModel)
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
