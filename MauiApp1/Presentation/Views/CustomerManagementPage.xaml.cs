using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class CustomerManagementPage : ContentPage
{
    public CustomerManagementPage(CustomerManagementViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is CustomerManagementViewModel viewModel)
            await viewModel.LoadAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        if (Navigation.ModalStack.Count > 0) await Navigation.PopModalAsync();
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }
}
