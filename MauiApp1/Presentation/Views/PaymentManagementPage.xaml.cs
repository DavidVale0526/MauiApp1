using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class PaymentManagementPage : ContentPage
{
    public PaymentManagementPage(PaymentManagementViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PaymentManagementViewModel viewModel)
            await viewModel.LoadAsync();
    }

    private async void OnReviewPaymentClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is not PaymentManagementViewModel viewModel) return;
        var payment = viewModel.PreparePayment();
        if (payment is null) return;

        var accepted = await DisplayAlertAsync("Confirmar pago", viewModel.BuildReviewText(), "Registrar", "Volver");
        if (!accepted)
        {
            viewModel.CancelReview();
            return;
        }

        await viewModel.RecordPendingAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is PaymentManagementViewModel viewModel) await viewModel.RefreshAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        if (Navigation.ModalStack.Count > 0) await Navigation.PopModalAsync();
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }
}
