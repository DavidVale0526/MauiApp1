using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Products;
using MauiApp1.Domain.Inventory;
using MauiApp1.Domain.Values;

namespace MauiApp1.Presentation.ViewModels;

public sealed class OtherExitViewModel(
    IProductService productService,
    IInventoryMovementService movementService) : INotifyPropertyChanged
{
    private ProductSummary? _selectedProduct;
    private InventoryExitTypeOption? _selectedExitType;
    private OtherExitReasonOption? _selectedReason;
    private string _quantityInput = string.Empty;
    private string _otherReasonDetail = string.Empty;
    private string _mixedWasteUnit = "libra";
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private InventoryOperationDraft? _pendingOperation;
    private bool _pendingAttempted;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProductSummary> Products { get; } = [];

    public IReadOnlyList<InventoryExitTypeOption> ExitTypeOptions { get; } =
    [
        new("Salida de producto", false),
        new("Merma mixta (no atribuida)", true)
    ];

    public IReadOnlyList<OtherExitReasonOption> ReasonOptions { get; } =
    [
        new("Vencimiento", false),
        new("Daño", false),
        new("Pérdida", false),
        new("Otro", true)
    ];

    public ProductSummary? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetProperty(ref _selectedProduct, value))
            {
                OnPropertyChanged(nameof(AvailableStockText));
            }
        }
    }

    public InventoryExitTypeOption? SelectedExitType
    {
        get => _selectedExitType;
        set
        {
            if (SetProperty(ref _selectedExitType, value))
            {
                OnPropertyChanged(nameof(IsMixedWaste));
                OnPropertyChanged(nameof(IsProductExit));
                OnPropertyChanged(nameof(AvailableStockTitle));
                OnPropertyChanged(nameof(AvailableStockText));
            }
        }
    }

    public OtherExitReasonOption? SelectedReason
    {
        get => _selectedReason;
        set
        {
            if (SetProperty(ref _selectedReason, value))
            {
                OnPropertyChanged(nameof(IsOtherReason));
            }
        }
    }

    public string QuantityInput
    {
        get => _quantityInput;
        set => SetProperty(ref _quantityInput, value);
    }

    public string OtherReasonDetail
    {
        get => _otherReasonDetail;
        set => SetProperty(ref _otherReasonDetail, value);
    }

    public string MixedWasteUnit
    {
        get => _mixedWasteUnit;
        set => SetProperty(ref _mixedWasteUnit, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(SubmitButtonText));
            }
        }
    }

    public bool IsOtherReason => SelectedReason?.RequiresDescription == true;

    public bool IsMixedWaste => SelectedExitType?.IsMixedWaste == true;

    public bool IsProductExit => !IsMixedWaste;

    public bool CanEdit => !IsBusy && _pendingOperation is null;

    public string SubmitButtonText => _pendingAttempted ? "Reintentar guardado" : "Revisar y confirmar salida";

    public string AvailableStockTitle => IsMixedWaste ? "Merma mixta" : "Existencia disponible";

    public string AvailableStockText => IsMixedWaste
        ? "Se registra aparte y no se asigna a un producto."
        : SelectedProduct is null
        ? "Existencia disponible: selecciona un producto."
        : $"Existencia disponible: {SelectedProduct.Stock.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {SelectedProduct.Unit}";

    public async Task LoadAsync()
    {
        try
        {
            var selectedId = SelectedProduct?.Id;
            var products = await productService.GetAllAsync();
            Products.Clear();
            foreach (var product in products.Where(product => product.IsActive))
            {
                Products.Add(product);
            }

            SelectedProduct = Products.FirstOrDefault(product => product.Id == selectedId) ?? Products.FirstOrDefault();
            SelectedExitType ??= ExitTypeOptions[0];
            ErrorMessage = Products.Count == 0 ? "No hay productos activos para registrar una salida." : string.Empty;
        }
        catch
        {
            ErrorMessage = "No se pudieron cargar los productos. Inténtalo de nuevo.";
        }
    }

    public InventoryOperationDraft? PrepareOperation()
    {
        if (_pendingOperation is not null)
        {
            return _pendingOperation;
        }

        ErrorMessage = string.Empty;
        if (!IsMixedWaste && SelectedProduct is null)
        {
            ErrorMessage = "Selecciona un producto activo.";
            return null;
        }

        if (IsMixedWaste && string.IsNullOrWhiteSpace(MixedWasteUnit))
        {
            ErrorMessage = "Especifica la unidad en la que pesaste la merma mixta.";
            return null;
        }

        if (!decimal.TryParse(QuantityInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
        {
            ErrorMessage = "Escribe una cantidad válida usando el separador decimal de este dispositivo.";
            return null;
        }

        Quantity quantity;
        try
        {
            quantity = Quantity.FromDecimal(amount);
        }
        catch (ArgumentOutOfRangeException)
        {
            ErrorMessage = "La cantidad admite hasta 3 decimales.";
            return null;
        }
        catch (OverflowException)
        {
            ErrorMessage = "La cantidad supera el valor permitido.";
            return null;
        }

        if (quantity.MilliUnits <= 0)
        {
            ErrorMessage = "La cantidad debe ser mayor que cero.";
            return null;
        }

        if (!IsMixedWaste && quantity.MilliUnits > SelectedProduct!.Stock.MilliUnits)
        {
            ErrorMessage = $"Existencia insuficiente. Disponible: {SelectedProduct.Stock.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {SelectedProduct.Unit}.";
            return null;
        }

        if (!IsMixedWaste && SelectedReason is null)
        {
            ErrorMessage = "Selecciona el motivo de la salida.";
            return null;
        }

        var reason = IsMixedWaste
            ? string.IsNullOrWhiteSpace(OtherReasonDetail) ? "Merma mixta" : $"Merma mixta: {OtherReasonDetail.Trim()}"
            : SelectedReason!.RequiresDescription ? $"Otro: {OtherReasonDetail.Trim()}" : SelectedReason.Name;
        if (!IsMixedWaste && SelectedReason!.RequiresDescription && string.IsNullOrWhiteSpace(OtherReasonDetail))
        {
            ErrorMessage = "Describe la otra causa de la salida.";
            return null;
        }

        _pendingOperation = new InventoryOperationDraft(
            Guid.NewGuid(),
            DateTime.UtcNow,
            [new InventoryMovementLineDraft(
                0,
                IsMixedWaste ? default : SelectedProduct!.Id,
                IsMixedWaste ? InventoryMovementType.MixedWaste : InventoryMovementType.OtherExit,
                quantity,
                reason,
                IsMixedWaste ? "Merma mixta (no atribuida)" : SelectedProduct!.Name,
                IsMixedWaste ? MixedWasteUnit.Trim() : SelectedProduct!.Unit)],
            NegativeStockPolicy.Reject);
        _pendingAttempted = false;
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(SubmitButtonText));
        return _pendingOperation;
    }

    public string BuildReviewText()
    {
        if (_pendingOperation is null)
        {
            return string.Empty;
        }

        var line = _pendingOperation.Lines[0];
        return IsMixedWaste
            ? $"Tipo: Merma mixta no atribuida\nCantidad: {line.Quantity.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {line.UnitSnapshot}\nMotivo: {line.Reason}\n\nSe registrará para conciliación posterior; no se asignará a un producto."
            : $"Producto: {SelectedProduct!.Name}\nCantidad: {line.Quantity.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {SelectedProduct.Unit}\nMotivo: {line.Reason}\n\nEsta salida disminuirá las existencias.";
    }

    public void CancelReview()
    {
        if (_pendingAttempted) return;
        _pendingOperation = null;
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(SubmitButtonText));
    }

    public async Task RecordPendingAsync()
    {
        if (IsBusy || _pendingOperation is null) return;
        IsBusy = true;
        _pendingAttempted = true;
        OnPropertyChanged(nameof(SubmitButtonText));
        ErrorMessage = string.Empty;
        try
        {
            var result = await movementService.ApplyAsync(_pendingOperation);
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo registrar la salida.";
                return;
            }

            _pendingOperation = null;
            _pendingAttempted = false;
            QuantityInput = string.Empty;
            OtherReasonDetail = string.Empty;
            MixedWasteUnit = "libra";
            SelectedExitType = ExitTypeOptions[0];
            SelectedReason = null;
            ErrorMessage = result.AlreadyApplied
                ? "Esta salida ya estaba registrada; no se volvió a descontar existencia."
                : "Salida registrada y existencia actualizada.";
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(SubmitButtonText));
            await LoadAsync();
        }
        catch
        {
            ErrorMessage = "No se pudo registrar la salida. Reintenta sin cambiar sus datos para evitar duplicados.";
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
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record OtherExitReasonOption(string Name, bool RequiresDescription);

public sealed record InventoryExitTypeOption(string Name, bool IsMixedWaste);
