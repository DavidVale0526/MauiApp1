using MauiApp1.Application.Sales;
using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class SaleTicketPage : ContentPage
{
    public SaleTicketPage(SaleHistoryEntry sale)
    {
        InitializeComponent();
        BindingContext = new SaleTicketViewModel(sale);
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopAsync();
}
