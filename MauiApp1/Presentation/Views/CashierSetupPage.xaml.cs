using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class CashierSetupPage : ContentPage
{
    public CashierSetupPage(CashierSetupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.AccountCreated += OnAccountCreated;
    }

    private async void OnAccountCreated(object? sender, EventArgs eventArgs)
    {
        await Navigation.PopModalAsync();
    }
}
