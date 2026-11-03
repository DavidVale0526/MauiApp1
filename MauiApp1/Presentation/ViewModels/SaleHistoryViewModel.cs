using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MauiApp1.Application.Sales;
using MauiApp1.Application.Authentication;
using MauiApp1.Domain.Users;

namespace MauiApp1.Presentation.ViewModels;

public sealed class SaleHistoryViewModel(ISaleService saleService, IAuthenticationService authenticationService) : INotifyPropertyChanged
{
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SaleHistoryCard> Sales { get; } = [];

    public bool CanManageReversals => authenticationService.CurrentUser?.Role == UserRole.Administrator;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var history = await saleService.GetHistoryAsync();
            Sales.Clear();
            foreach (var sale in history)
            {
                Sales.Add(new SaleHistoryCard(sale, CanManageReversals));
            }
        }
        catch
        {
            ErrorMessage = "No se pudo cargar el historial de ventas. Inténtalo de nuevo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed record SaleHistoryCard(SaleHistoryEntry Sale, bool CanManageReversals)
{
    public string DateText => Sale.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string ActorText => $"Registró: {Sale.ActorUserName}";

    public string ReferenceText => $"Venta {Sale.OperationId.ToString("N")[..8]}";

    public string TotalText => $"Total: {(Sale.TotalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";

    public string SaleTypeText => Sale.IsCredit ? "Venta a crédito" : "Venta de contado";

    public string StatusText
    {
        get
        {
            var reversals = Sale.Reversals ?? [];
            if (reversals.Any(item => item.Kind == SaleReversalKind.Cancellation)) return "Anulada";
            if (reversals.Count == 0) return "Activa";
            var returned = reversals.SelectMany(item => item.Lines).GroupBy(item => item.SaleLineNumber)
                .ToDictionary(group => group.Key, group => group.Sum(line => line.QuantityMilliUnits));
            return Sale.Lines.Select((line, index) => returned.GetValueOrDefault(index) >= line.QuantityMilliUnits).All(value => value)
                ? "Devuelta" : "Parcialmente devuelta";
        }
    }

    public string ReversalDetailsText => string.Join(Environment.NewLine, (Sale.Reversals ?? []).Select(item =>
        $"{(item.Kind == SaleReversalKind.Cancellation ? "Anulación" : "Devolución")} · C$ {item.AmountMinorUnits / 100m:0.00} · {item.Reason} · {item.ActorUserName}"));

    public bool HasReversals => Sale.Reversals is { Count: > 0 };

    public bool CanCreateReversal => CanManageReversals && StatusText is not ("Anulada" or "Devuelta");

    public string PaymentText => Sale.IsCredit
        ? "Cuenta por cobrar; los abonos aparecen por separado."
        : Sale.Payments is not { Count: > 0 }
            ? "Sin detalle de pago registrado."
            : string.Join(" · ", Sale.Payments.Select(payment =>
                payment.Method switch
                {
                    SalePaymentMethod.Cash => $"Efectivo C$ {(payment.AmountMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)} (recibido C$ {(payment.CashReceivedMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}, cambio C$ {(payment.ChangeMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)})",
                    SalePaymentMethod.Card => $"Tarjeta C$ {(payment.AmountMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}",
                    SalePaymentMethod.Transfer => $"Transferencia C$ {(payment.AmountMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}",
                    _ => "Método no disponible"
                }));

    public string CustomerText => Sale.IsCredit ? $"Cliente: {Sale.CustomerName ?? "No disponible"}" : string.Empty;

    public string DueText => Sale.CreditDueAtUtc is { } due
        ? $"Vence: {due.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}"
        : string.Empty;

    public string LinesText => string.Join(
        Environment.NewLine,
        Sale.Lines.Select(line =>
            $"• {line.ProductName}: {(line.QuantityMilliUnits / 1000m).ToString("0.###", CultureInfo.CurrentCulture)} {line.Unit} × {(line.SalePriceMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)} = {(line.LineTotalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}"));
}
