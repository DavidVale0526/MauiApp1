using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MauiApp1.Application.Inventory;
using MauiApp1.Domain.Inventory;
using Microsoft.Maui.Graphics;

namespace MauiApp1.Presentation.ViewModels;

public sealed class InventoryMovementHistoryViewModel(IInventoryMovementService movementService) : INotifyPropertyChanged
{
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<InventoryMovementHistoryCard> Movements { get; } = [];

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
            var history = await movementService.GetHistoryAsync();
            Movements.Clear();
            foreach (var movement in history)
            {
                Movements.Add(new InventoryMovementHistoryCard(movement));
            }
        }
        catch
        {
            ErrorMessage = "No se pudo cargar el historial de movimientos. Inténtalo de nuevo.";
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

public sealed record InventoryMovementHistoryCard(InventoryMovementHistoryEntry Movement)
{
    public string TypeText => Movement.Type switch
    {
        InventoryMovementType.PurchaseEntry => "Entrada por compra",
        InventoryMovementType.SaleExit => "Salida por venta",
        InventoryMovementType.OtherExit => "Otra salida",
        _ => "Movimiento"
    };

    public Color TypeBackgroundColor => Movement.Type switch
    {
        InventoryMovementType.PurchaseEntry => Color.FromArgb("#E8F5EE"),
        InventoryMovementType.SaleExit => Color.FromArgb("#EAF1FF"),
        InventoryMovementType.OtherExit => Color.FromArgb("#FFF3E6"),
        _ => Color.FromArgb("#EEF1F5")
    };

    public Color TypeTextColor => Movement.Type switch
    {
        InventoryMovementType.PurchaseEntry => Color.FromArgb("#18794E"),
        InventoryMovementType.SaleExit => Color.FromArgb("#315CB8"),
        InventoryMovementType.OtherExit => Color.FromArgb("#A85B00"),
        _ => Color.FromArgb("#52657D")
    };

    public Color QuantityTextColor => Movement.Type == InventoryMovementType.PurchaseEntry
        ? Color.FromArgb("#18794E")
        : Color.FromArgb("#B42318");

    public string ProductText => Movement.ProductName;

    public string QuantityText
    {
        get
        {
            var sign = Movement.Type == InventoryMovementType.PurchaseEntry ? "+" : "−";
            return $"{sign}{(Movement.QuantityMilliUnits / 1000m).ToString("0.000", CultureInfo.CurrentCulture)} {Movement.Unit}";
        }
    }

    public string DateText => Movement.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string ReasonText => string.IsNullOrWhiteSpace(Movement.Reason) ? "Sin motivo adicional" : Movement.Reason;

    public string ReferenceText => $"Operación {Movement.OperationId.ToString("N")[..8]}";
}
