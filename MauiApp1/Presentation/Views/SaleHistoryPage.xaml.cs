using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;
using MauiApp1.Presentation.Services;
using MauiApp1.Application.Sales;
using MauiApp1.Domain.Values;
using System.Globalization;

namespace MauiApp1.Presentation.Views;

public partial class SaleHistoryPage : CoordinatorRefreshPage
{
    private readonly SaleHistoryViewModel _viewModel;
    private readonly IReceiptDocumentActions _documentActions;
    private readonly ISaleService _saleService;
    private bool _reversalBusy;

    public SaleHistoryPage(SaleHistoryViewModel viewModel, ILocalDataChangeNotifier changeNotifier,
        IReceiptDocumentActions documentActions, ISaleService saleService) : base(changeNotifier)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _documentActions = documentActions;
        _saleService = saleService;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.LoadAsync();

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopModalAsync();

    private async void OnRefreshClicked(object? sender, EventArgs eventArgs) => await _viewModel.LoadAsync();

    private async void OnViewTicketClicked(object? sender, EventArgs eventArgs)
    {
        if ((sender as Button)?.BindingContext is not SaleHistoryCard card)
        {
            return;
        }

        await Navigation.PushAsync(new SaleTicketPage(card.Sale, _documentActions));
    }

    private async void OnReverseSaleClicked(object? sender, EventArgs eventArgs)
    {
        if (_reversalBusy || (sender as Button)?.BindingContext is not SaleHistoryCard { CanCreateReversal: true } card) return;
        var choice = await DisplayActionSheetAsync("Corregir venta", "Cancelar", null,
            "Anular venta completa", "Registrar devolución parcial");
        if (choice is not ("Anular venta completa" or "Registrar devolución parcial")) return;
        var kind = choice == "Anular venta completa" ? SaleReversalKind.Cancellation : SaleReversalKind.Return;
        if (kind == SaleReversalKind.Cancellation && card.HasReversals)
        {
            await DisplayAlertAsync("No disponible", "Una venta con devoluciones parciales ya no se puede anular completa.", "Aceptar");
            return;
        }

        var reason = await DisplayPromptAsync(kind == SaleReversalKind.Cancellation ? "Motivo de anulación" : "Motivo de devolución",
            "Escribe el motivo. Quedará guardado en la trazabilidad de la venta.", "Continuar", "Cancelar", "Motivo obligatorio", maxLength: 500);
        if (string.IsNullOrWhiteSpace(reason)) return;

        var prior = (card.Sale.Reversals ?? []).SelectMany(item => item.Lines).GroupBy(item => item.SaleLineNumber)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.QuantityMilliUnits));
        var priorAmounts = (card.Sale.Reversals ?? []).SelectMany(item => item.Lines).GroupBy(item => item.SaleLineNumber)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.AmountMinorUnits));
        var requestedLines = new List<SaleReversalLineDraft>();
        long refundTotal = 0;
        for (var index = 0; index < card.Sale.Lines.Count; index++)
        {
            var line = card.Sale.Lines[index];
            var remaining = line.QuantityMilliUnits - prior.GetValueOrDefault(index);
            if (remaining <= 0) continue;
            long quantity;
            if (kind == SaleReversalKind.Cancellation)
            {
                quantity = remaining;
            }
            else
            {
                var text = await DisplayPromptAsync("Cantidad a devolver",
                    $"{line.ProductName}\nDisponible para devolución: {remaining / 1000m:0.###} {line.Unit}",
                    "Agregar", "Omitir", "Cantidad (hasta 3 decimales)", initialValue: string.Empty,
                    maxLength: 20, keyboard: Keyboard.Numeric);
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
                {
                    await DisplayAlertAsync("Cantidad inválida", "Escribe una cantidad mayor que cero con hasta tres decimales.", "Aceptar");
                    return;
                }
                try { quantity = Quantity.FromDecimal(amount).MilliUnits; }
                catch (Exception) { await DisplayAlertAsync("Cantidad inválida", "La cantidad admite hasta tres decimales.", "Aceptar"); return; }
                if (quantity > remaining)
                {
                    await DisplayAlertAsync("Cantidad excedida", "La cantidad supera lo que queda disponible para devolver.", "Aceptar");
                    return;
                }
            }

            var disposition = await DisplayActionSheetAsync($"¿Qué ocurre con {line.ProductName}?", "Cancelar", null,
                "Regresa al inventario vendible", "No regresa al inventario");
            if (disposition == "Cancelar" || string.IsNullOrWhiteSpace(disposition)) return;
            var returnToStock = disposition == "Regresa al inventario vendible";
            string? disposalReason = null;
            if (!returnToStock)
            {
                disposalReason = await DisplayPromptAsync("Motivo de disposición", "Ej.: producto dañado o no apto para venta.",
                    "Continuar", "Cancelar", "Motivo obligatorio", maxLength: 500);
                if (string.IsNullOrWhiteSpace(disposalReason)) return;
            }
            requestedLines.Add(new SaleReversalLineDraft(index, quantity, returnToStock, disposalReason));
            try
            {
                var amountAfterReturn = SaleAmountCalculator.CalculateLineTotalMinorUnits(
                    new MoneyAmount(line.SalePriceMinorUnits), new Quantity(checked(prior.GetValueOrDefault(index) + quantity)));
                refundTotal = checked(refundTotal + amountAfterReturn - priorAmounts.GetValueOrDefault(index));
            }
            catch (OverflowException)
            {
                await DisplayAlertAsync("Importe inválido", "El total de la devolución supera el límite permitido.", "Aceptar");
                return;
            }
        }
        if (requestedLines.Count == 0)
        {
            await DisplayAlertAsync("Sin productos", "Selecciona al menos un renglón para devolver.", "Aceptar");
            return;
        }

        SaleRefundMethod? refundMethod = null;
        if (!card.Sale.IsCredit)
        {
            var method = await DisplayActionSheetAsync("Método del reembolso", "Cancelar", null, "Efectivo", "Transferencia", "Tarjeta");
            refundMethod = method switch
            {
                "Efectivo" => SaleRefundMethod.Cash,
                "Transferencia" => SaleRefundMethod.Transfer,
                "Tarjeta" => SaleRefundMethod.Card,
                _ => null
            };
            if (refundMethod is null) return;
        }

        var financialEffect = card.Sale.IsCredit
            ? $"Se reducirá la cuenta por cobrar en C$ {refundTotal / 100m:0.00}."
            : $"Reembolso declarado: C$ {refundTotal / 100m:0.00} por {refundMethod}.";
        var confirm = await DisplayAlertAsync(kind == SaleReversalKind.Cancellation ? "Confirmar anulación" : "Confirmar devolución",
            $"{requestedLines.Count} renglón(es). {financialEffect}\n\nEsta operación quedará vinculada a la venta original.",
            "Confirmar", "Volver");
        if (!confirm) return;

        _reversalBusy = true;
        try
        {
            var result = await _saleService.RecordReversalAsync(new SaleReversalDraft(Guid.NewGuid(), card.Sale.OperationId,
                DateTime.UtcNow, kind, reason, requestedLines, refundMethod));
            if (!result.Succeeded)
            {
                await DisplayAlertAsync("No se registró", result.ErrorMessage ?? "No se pudo completar la corrección.", "Aceptar");
                return;
            }
            await DisplayAlertAsync("Operación registrada", "La reversión se guardó y quedó en la cola de sincronización del dispositivo.", "Aceptar");
            await _viewModel.LoadAsync();
        }
        catch (Exception)
        {
            await DisplayAlertAsync("No se pudo completar", "No se confirmó el registro de la corrección. Actualiza el historial antes de volver a intentarlo; la venta original se conserva.", "Aceptar");
            await _viewModel.LoadAsync();
        }
        finally
        {
            _reversalBusy = false;
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs eventArgs)
    {
        var isWide = Width >= 900;
        if (sender is Grid pageLayout)
        {
            pageLayout.Padding = isWide ? new Thickness(32, 28) : new Thickness(16, 20);
        }
    }
}
