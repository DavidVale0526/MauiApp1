using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class SaleHistoryPage : ContentPage
{
    private readonly SaleHistoryViewModel _viewModel;

    public SaleHistoryPage(SaleHistoryViewModel viewModel)
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
