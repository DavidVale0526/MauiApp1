using MauiApp1.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MauiApp1.Presentation.Views;

public partial class MainPage : ContentPage
{
    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnCreateCashierAccountClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is not MainPageViewModel { IsAdministrator: true } ||
            Handler?.MauiContext?.Services.GetRequiredService<CashierSetupPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnManageCategoriesClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<CategoryManagementPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnManageProductsClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<ProductManagementPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnConsultStockClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<StockConsultationPage>() is not { } page)
        {
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }
}
