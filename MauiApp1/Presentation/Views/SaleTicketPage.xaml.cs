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

    private void OnPageSizeChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is Grid layout)
        {
            layout.Padding = Width >= 900 ? new Thickness(32, 28) : new Thickness(16, 20);
            TicketLayout.WidthRequest = Math.Min(720, Math.Max(0, layout.Width - layout.Padding.HorizontalThickness));
        }
    }
}
