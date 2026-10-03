using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class OtherExitPage : CoordinatorRefreshPage
{
    private readonly OtherExitViewModel _viewModel;

    public OtherExitPage(OtherExitViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
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

    protected override Task RefreshAfterCoordinatorChangesAsync() =>
        _viewModel.CanEdit ? _viewModel.LoadAsync() : Task.CompletedTask;

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
