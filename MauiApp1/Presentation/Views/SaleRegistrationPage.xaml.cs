using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class SaleRegistrationPage : ContentPage
{
    private readonly SaleRegistrationViewModel _viewModel;

    public SaleRegistrationPage(SaleRegistrationViewModel viewModel)
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

    private void OnRemoveLineClicked(object? sender, EventArgs eventArgs)
    {
        if ((sender as Button)?.BindingContext is SaleCartLine line)
        {
            _viewModel.RemoveLine(line);
        }
    }

    private async void OnReviewClicked(object? sender, EventArgs eventArgs)
    {
        var sale = _viewModel.PrepareSale();
        if (sale is null)
        {
            return;
        }

        var retry = _viewModel.ConfirmButtonText.StartsWith("Reintentar", StringComparison.Ordinal);
        var confirmed = await DisplayAlertAsync(
            retry ? "Reintentar venta" : "Confirmar venta",
            _viewModel.BuildReviewText(),
            retry ? "Reintentar" : "Registrar venta",
            retry ? "Cancelar" : "Volver a revisar");
        if (!confirmed)
        {
            _viewModel.CancelReview();
            return;
        }

        await _viewModel.RecordPendingAsync();
    }
}
