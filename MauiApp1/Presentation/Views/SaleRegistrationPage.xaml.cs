using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;
using MauiApp1.Application.Sales;

namespace MauiApp1.Presentation.Views;

public partial class SaleRegistrationPage : CoordinatorRefreshPage
{
    private void OnSaleLayoutSizeChanged(object? sender, EventArgs args)
    {
        var useDesktopLayout = SaleLayout.Width >= 920;
        ContentPanels.ColumnDefinitions.Clear();
        ContentPanels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        if (useDesktopLayout)
        {
            ContentPanels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        Grid.SetColumn(ProductPanel, 0);
        Grid.SetRow(ProductPanel, 0);
        Grid.SetColumn(CartPanel, useDesktopLayout ? 1 : 0);
        Grid.SetRow(CartPanel, useDesktopLayout ? 0 : 1);
        CartCollection.HeightRequest = useDesktopLayout ? 560 : 300;
        SaleLayout.Padding = useDesktopLayout ? new Thickness(32, 28) : new Thickness(16, 20);
    }

    private readonly SaleRegistrationViewModel _viewModel;
    private readonly ISaleService _saleService;

    public SaleRegistrationPage(SaleRegistrationViewModel viewModel, ISaleService saleService,
        ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _saleService = saleService;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.RefreshAfterCoordinatorChangesAsync();

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

        if (await _viewModel.RecordPendingAsync())
        {
            await ShowSavedTicketAsync();
        }
    }

    private async Task ShowSavedTicketAsync()
    {
        if (_viewModel.LastCompletedSaleOperationId is not { } operationId)
        {
            return;
        }

        try
        {
            var sale = (await _saleService.GetHistoryAsync())
                .SingleOrDefault(item => item.OperationId == operationId);
            if (sale is not null)
            {
                await Navigation.PushAsync(new SaleTicketPage(sale));
                return;
            }
        }
        catch
        {
            // The sale is already saved; the history page remains available to reopen its ticket.
        }

        await DisplayAlertAsync(
            "Venta guardada",
            "La venta se guardó, pero no se pudo abrir el ticket ahora. Puedes consultarlo desde el historial de ventas.",
            "Aceptar");
    }
}
