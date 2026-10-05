using System.Globalization;
using MauiApp1.Application.Sales;
using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class SaleReviewPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _finishing;

    public SaleReviewPage(SaleRegistrationViewModel viewModel, SaleDraft sale, bool retry)
    {
        InitializeComponent();
        BindingContext = viewModel;
        ReviewTitle.Text = retry ? "Reintentar venta" : "Confirmar venta";
        ConfirmButton.Text = retry ? "Reintentar" : "Registrar venta";
        CancelButton.Text = retry ? "Cancelar" : "Volver a revisar";
        TermsText.Text = sale.IsCredit
            ? $"Venta a crédito para: {viewModel.SelectedCustomer?.Name ?? "cliente"}\nVence: {sale.CreditDueAtUtc!.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}\nEl saldo inicial será {viewModel.TotalAmountText}."
            : "Venta de contado.";
    }

    public Task<bool> Decision => _decision.Task;

    private async void OnConfirmClicked(object? sender, EventArgs args) => await FinishAsync(true);
    private async void OnCancelClicked(object? sender, EventArgs args) => await FinishAsync(false);

    private async Task FinishAsync(bool confirmed)
    {
        if (_finishing) return;
        _finishing = true;
        ConfirmButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        try
        {
            await Navigation.PopModalAsync();
            _decision.TrySetResult(confirmed);
        }
        catch
        {
            _decision.TrySetResult(false);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = FinishAsync(false);
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_finishing && !Navigation.ModalStack.Contains(this)) _decision.TrySetResult(false);
    }

    private void OnReviewSizeChanged(object? sender, EventArgs args)
    {
        if (ReviewRoot.Width <= 0 || ReviewRoot.Height <= 0) return;
        var isDesktop = ReviewRoot.Width >= 900;
        ReviewRoot.Padding = isDesktop ? new Thickness(28) : new Thickness(12, 20);
        ReviewSurface.WidthRequest = Math.Min(700, ReviewRoot.Width - ReviewRoot.Padding.HorizontalThickness);
        ReviewSurface.HeightRequest = Math.Min(800, ReviewRoot.Height - ReviewRoot.Padding.VerticalThickness);
        ReviewSurface.VerticalOptions = isDesktop ? LayoutOptions.Center : LayoutOptions.End;
    }
}
