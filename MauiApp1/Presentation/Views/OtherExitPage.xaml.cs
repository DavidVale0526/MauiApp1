using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class OtherExitPage : ContentPage
{
    private readonly OtherExitViewModel _viewModel;

    public OtherExitPage(OtherExitViewModel viewModel)
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

    private async void OnReviewClicked(object? sender, EventArgs eventArgs)
    {
        var operation = _viewModel.PrepareOperation();
        if (operation is null)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Confirmar salida",
            _viewModel.BuildReviewText(),
            "Registrar salida",
            "Volver a revisar");
        if (!confirmed)
        {
            _viewModel.CancelReview();
            return;
        }

        await _viewModel.RecordPendingAsync();
    }
}
