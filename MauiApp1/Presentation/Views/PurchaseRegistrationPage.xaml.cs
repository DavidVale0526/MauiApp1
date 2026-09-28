using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class PurchaseRegistrationPage : ContentPage
{
    private readonly PurchaseRegistrationViewModel _viewModel;

    public PurchaseRegistrationPage(PurchaseRegistrationViewModel viewModel)
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
        if ((sender as Button)?.BindingContext is PurchaseCartLine line)
        {
            _viewModel.RemoveLine(line);
        }
    }

    private async void OnReviewClicked(object? sender, EventArgs eventArgs)
    {
        var purchase = _viewModel.PreparePurchase();
        if (purchase is null)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Confirmar compra",
            _viewModel.BuildReviewText(),
            "Confirmar",
            "Volver a revisar");
        if (!confirmed)
        {
            _viewModel.CancelReview();
            return;
        }

        await _viewModel.RecordPendingAsync();
    }
}
