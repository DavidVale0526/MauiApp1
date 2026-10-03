using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class PaymentManagementPage : CoordinatorRefreshPage
{
    private readonly PaymentManagementViewModel _viewModel;

    public PaymentManagementPage(PaymentManagementViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() =>
        string.IsNullOrWhiteSpace(_viewModel.AmountInput) && _viewModel.CanEditPayment
            ? _viewModel.LoadAsync()
            : Task.CompletedTask;

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
