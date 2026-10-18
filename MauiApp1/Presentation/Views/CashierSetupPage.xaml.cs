using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class CashierSetupPage : ContentPage
{
    private readonly CashierSetupViewModel _viewModel;

    public CashierSetupPage(CashierSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCashiersAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        await Navigation.PopModalAsync();
    }
}
