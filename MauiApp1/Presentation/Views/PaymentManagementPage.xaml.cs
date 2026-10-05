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

    private void OnPageSizeChanged(object? sender, EventArgs eventArgs)
    {
        var isWide = Width >= 900;
        PageLayout.Padding = isWide ? new Thickness(32, 28) : new Thickness(16, 20);

        ContentLayout.ColumnDefinitions.Clear();
        ContentLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (isWide)
            ContentLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        ContentLayout.RowDefinitions.Clear();
        ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        ContentLayout.RowDefinitions.Add(new RowDefinition(isWide ? GridLength.Star : GridLength.Auto));
        if (!isWide)
            ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetRow(HeaderArea, 0);
        Grid.SetColumn(HeaderArea, 0);
        Grid.SetColumnSpan(HeaderArea, isWide ? 2 : 1);
        Grid.SetRow(PaymentFormCard, 1);
        Grid.SetColumn(PaymentFormCard, 0);
        Grid.SetRow(PaymentHistoryCard, isWide ? 1 : 2);
        Grid.SetColumn(PaymentHistoryCard, isWide ? 1 : 0);
        PaymentsCollection.HeightRequest = isWide ? 560 : 420;
    }
}
