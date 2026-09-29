using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MauiApp1.Application.Sales;

namespace MauiApp1.Presentation.ViewModels;

public sealed class SaleHistoryViewModel(ISaleService saleService) : INotifyPropertyChanged
{
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SaleHistoryCard> Sales { get; } = [];

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
                Sales.Add(new SaleHistoryCard(sale));
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

public sealed record SaleHistoryCard(SaleHistoryEntry Sale)
{
    public string DateText => Sale.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string ActorText => $"Registró: {Sale.ActorUserName}";

    public string ReferenceText => $"Venta {Sale.OperationId.ToString("N")[..8]}";

    public string TotalText => $"Total: {(Sale.TotalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";

    public string LinesText => string.Join(
        Environment.NewLine,
        Sale.Lines.Select(line =>
            $"• {line.ProductName}: {(line.QuantityMilliUnits / 1000m).ToString("0.000", CultureInfo.CurrentCulture)} {line.Unit} × {(line.SalePriceMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)} = {(line.LineTotalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}"));
}
