using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Products;
using MauiApp1.Application.Purchases;
using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Values;

namespace MauiApp1.Presentation.ViewModels;

public sealed class PurchaseRegistrationViewModel : INotifyPropertyChanged
{
    private readonly IProductService _productService;
    private readonly IPurchaseService _purchaseService;
    private PurchaseProductOption? _selectedProduct;
    private string _quantity = string.Empty;
    private string _selectedPurchaseUnit = string.Empty;
    private string _otherPurchaseUnit = string.Empty;
    private string _conversionFactorInput = "1";
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private PurchaseDraft? _pendingPurchase;
    private bool _pendingPurchaseAttempted;

    public PurchaseRegistrationViewModel(IProductService productService, IPurchaseService purchaseService)
    {
        _productService = productService;
        _purchaseService = purchaseService;
        AddLineCommand = new Command(AddLine, () => !IsBusy && _pendingPurchase is null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PurchaseProductOption> Products { get; } = [];

    public ObservableCollection<string> PurchaseUnitOptions { get; } =
    [
        "libra", "kilogramo", "gramo", "quintal", "arroba", "tonelada",
        "litro", "mililitro", "galón", "unidad", "pieza", "caja", "saco", "paquete", "Otra unidad"
    ];

    public ObservableCollection<PurchaseCartLine> Lines { get; } = [];

    public ICommand AddLineCommand { get; }

    public PurchaseProductOption? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetProperty(ref _selectedProduct, value))
            {
                if (value is not null)
                {
                    if (!PurchaseUnitOptions.Contains(value.Unit, StringComparer.OrdinalIgnoreCase))
                        PurchaseUnitOptions.Insert(0, value.Unit);
                    SelectedPurchaseUnit = value.Unit;
                }
                OnPropertyChanged(nameof(StockUnit));
                OnPropertyChanged(nameof(StockUnitDescription));
                OnPropertyChanged(nameof(ConversionPreviewText));
            }
        }
    }

    public string QuantityInput
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, value)) OnPropertyChanged(nameof(ConversionPreviewText));
        }
    }

    public string SelectedPurchaseUnit
    {
        get => _selectedPurchaseUnit;
        set
        {
            value ??= _selectedPurchaseUnit;
            if (SetProperty(ref _selectedPurchaseUnit, value))
            {
                if (value == "Otra unidad") OtherPurchaseUnit = string.Empty;
                else
                {
                    OtherPurchaseUnit = string.Empty;
                    ConversionFactorInput = InferConversionFactor(value, StockUnit)?.ToString("0.######", CultureInfo.CurrentCulture) ?? string.Empty;
                }
                OnPropertyChanged(nameof(IsCustomPurchaseUnit));
                OnPropertyChanged(nameof(EffectivePurchaseUnit));
                OnPropertyChanged(nameof(ConversionPreviewText));
            }
        }
    }

    public string OtherPurchaseUnit
    {
        get => _otherPurchaseUnit;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _otherPurchaseUnit, value))
            {
                OnPropertyChanged(nameof(EffectivePurchaseUnit));
                OnPropertyChanged(nameof(ConversionPreviewText));
            }
        }
    }

    public string ConversionFactorInput
    {
        get => _conversionFactorInput;
        set
        {
            if (SetProperty(ref _conversionFactorInput, value)) OnPropertyChanged(nameof(ConversionPreviewText));
        }
    }

    public string StockUnit => SelectedProduct?.Unit ?? string.Empty;
    public string StockUnitDescription => string.IsNullOrWhiteSpace(StockUnit)
        ? "Elige el producto: su unidad será la unidad de inventario y de venta."
        : $"Este producto se controla y se vende en {StockUnit}.";
    public bool IsCustomPurchaseUnit => SelectedPurchaseUnit == "Otra unidad";
    public string EffectivePurchaseUnit => IsCustomPurchaseUnit ? OtherPurchaseUnit.Trim() : SelectedPurchaseUnit;

    public string ConversionPreviewText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(EffectivePurchaseUnit) || string.IsNullOrWhiteSpace(StockUnit) ||
                !decimal.TryParse(ConversionFactorInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var factor) || factor <= 0)
                return "Indica cuántas unidades de inventario equivalen a 1 unidad recibida.";

            if (!decimal.TryParse(QuantityInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var received) || received <= 0)
                return $"1 {EffectivePurchaseUnit} = {factor.ToString("0.###", CultureInfo.CurrentCulture)} {StockUnit}";

            var converted = decimal.Round(received * factor, 3, MidpointRounding.AwayFromZero);
            return $"{received.ToString("0.###", CultureInfo.CurrentCulture)} {EffectivePurchaseUnit} = {converted.ToString("0.###", CultureInfo.CurrentCulture)} {StockUnit}";
        }
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
                ((Command)AddLineCommand).ChangeCanExecute();
                OnPropertyChanged(nameof(CanEditLines));
            }
        }
    }

    public bool CanEditLines => !IsBusy && _pendingPurchase is null;

    public async Task LoadAsync()
    {
        var selectedId = SelectedProduct?.Id;
        var previousStockUnit = StockUnit;
        var purchaseUnit = SelectedPurchaseUnit;
        var customUnit = OtherPurchaseUnit;
        var conversion = ConversionFactorInput;
        try
        {
            var products = await _productService.GetAllAsync();
            Products.Clear();
            foreach (var product in products.Where(item => item.IsActive))
            {
                Products.Add(new PurchaseProductOption(product.Id, product.Name, product.Unit));
            }

            SelectedProduct = Products.FirstOrDefault(item => item.Id == selectedId) ?? Products.FirstOrDefault();
            if (selectedId is not null && SelectedProduct?.Id == selectedId && StockUnit == previousStockUnit)
            {
                SelectedPurchaseUnit = purchaseUnit;
                OtherPurchaseUnit = customUnit;
                ConversionFactorInput = conversion;
            }
            ErrorMessage = Products.Count == 0 ? "No hay productos activos para agregar a una compra." : string.Empty;
        }
        catch
        {
            ErrorMessage = "No se pudieron cargar los productos activos. Inténtalo de nuevo.";
        }
    }

    public async Task RefreshAfterCoordinatorChangesAsync()
    {
        if (_pendingPurchase is not null || IsBusy) return;
        var selectedProductId = SelectedProduct?.Id;
        await LoadAsync();
        SelectedProduct = Products.FirstOrDefault(item => item.Id == selectedProductId) ?? Products.FirstOrDefault();
    }

    public void RemoveLine(PurchaseCartLine? line)
    {
        if (CanEditLines && line is not null)
        {
            Lines.Remove(line);
            ErrorMessage = string.Empty;
        }
    }

    public PurchaseDraft? PreparePurchase()
    {
        if (Lines.Count == 0)
        {
            ErrorMessage = "Agrega al menos un producto antes de revisar la compra.";
            return null;
        }

        if (_pendingPurchase is null)
        {
            _pendingPurchase = new PurchaseDraft(
                Guid.NewGuid(),
                DateTime.UtcNow,
                Lines.Select(line => new PurchaseLineDraft(
                    line.ProductId,
                    line.Quantity,
                    line.Name,
                    line.Unit)).ToArray());
            _pendingPurchaseAttempted = false;
            OnPropertyChanged(nameof(CanEditLines));
            ((Command)AddLineCommand).ChangeCanExecute();
        }

        return _pendingPurchase;
    }

    public string BuildReviewText()
    {
        if (_pendingPurchase is null)
        {
            return string.Empty;
        }

        var lines = Lines.Select(line => $"• {line.Name}: {line.Quantity.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {line.Unit}\n  Conversión: {line.ConversionText}");
        return $"Se registrarán estas cantidades y se sumarán a las existencias:\n\n{string.Join("\n", lines)}\n\nLa fecha y hora se registrarán automáticamente.";
    }

    public void CancelReview()
    {
        if (_pendingPurchaseAttempted)
        {
            return;
        }

        _pendingPurchase = null;
        OnPropertyChanged(nameof(CanEditLines));
        ((Command)AddLineCommand).ChangeCanExecute();
    }

    public async Task<bool> RecordPendingAsync()
    {
        if (IsBusy || _pendingPurchase is null)
        {
            return false;
        }

        IsBusy = true;
        _pendingPurchaseAttempted = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _purchaseService.RecordAsync(_pendingPurchase);
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo guardar la compra.";
                return false;
            }

            Lines.Clear();
            _pendingPurchase = null;
            _pendingPurchaseAttempted = false;
            ErrorMessage = result.AlreadyApplied
                ? "Esta compra ya se había guardado; no se duplicó la existencia."
                : "Compra guardada y existencias actualizadas.";
            OnPropertyChanged(nameof(CanEditLines));
            ((Command)AddLineCommand).ChangeCanExecute();
            return true;
        }
        catch
        {
            ErrorMessage = "No se pudo guardar la compra. Reintenta sin cambiar el contenido para evitar duplicados.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddLine()
    {
        ErrorMessage = string.Empty;
        if (SelectedProduct is null)
        {
            ErrorMessage = "Selecciona un producto activo.";
            return;
        }

        if (string.IsNullOrWhiteSpace(EffectivePurchaseUnit))
        {
            ErrorMessage = "Selecciona o escribe la unidad en que recibiste el producto.";
            return;
        }

        if (!decimal.TryParse(QuantityInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
        {
            ErrorMessage = "Escribe una cantidad válida usando el separador decimal de este dispositivo.";
            return;
        }

        if (!decimal.TryParse(ConversionFactorInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var factor) || factor <= 0)
        {
            ErrorMessage = "Indica una equivalencia mayor que cero para convertir a la unidad de inventario.";
            return;
        }

        Quantity receivedQuantity;
        Quantity parsed;
        try
        {
            receivedQuantity = Quantity.FromDecimal(amount);
            if (receivedQuantity.MilliUnits <= 0)
            {
                ErrorMessage = "La cantidad debe ser mayor que cero.";
                return;
            }

            parsed = Quantity.FromDecimal(decimal.Round(receivedQuantity.ToDecimal() * factor, 3, MidpointRounding.AwayFromZero));
            if (parsed.MilliUnits <= 0)
            {
                ErrorMessage = "La cantidad convertida debe ser mayor que cero.";
                return;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            ErrorMessage = "La cantidad admite hasta 3 decimales.";
            return;
        }
        catch (OverflowException)
        {
            ErrorMessage = "La cantidad supera el valor permitido.";
            return;
        }

        var conversionText = $"{receivedQuantity.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {EffectivePurchaseUnit} → {parsed.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {SelectedProduct.Unit}";
        var existing = Lines.FirstOrDefault(line => line.ProductId == SelectedProduct.Id);
        try
        {
            if (existing is null)
            {
                Lines.Add(new PurchaseCartLine(SelectedProduct.Id, SelectedProduct.Name, SelectedProduct.Unit, parsed, conversionText));
            }
            else
            {
                var total = checked(existing.Quantity.MilliUnits + parsed.MilliUnits);
                var index = Lines.IndexOf(existing);
                Lines[index] = existing with { Quantity = new Quantity(total), ConversionText = $"{existing.ConversionText}; {conversionText}" };
            }
        }
        catch (OverflowException)
        {
            ErrorMessage = "La cantidad acumulada de este producto supera el valor permitido.";
            return;
        }

        QuantityInput = string.Empty;
    }

    private static decimal? InferConversionFactor(string? fromUnit, string? toUnit)
    {
        var from = NormalizeUnit(fromUnit);
        var to = NormalizeUnit(toUnit);
        if (from.Length == 0 || to.Length == 0) return null;
        if (from == to) return 1m;

        decimal? poundsPerUnit = from switch
        {
            "quintal" => 100m,
            "arroba" => 25m,
            "kilogramo" => 2.204622m,
            "gramo" => 0.002204622m,
            "tonelada" => 2204.622m,
            "onza" => 0.0625m,
            _ => null
        };
        var targetIsPound = to is "libra" or "libra(s)";
        if (targetIsPound && poundsPerUnit is not null) return poundsPerUnit;
        if (from is "libra" or "libra(s)")
        {
            return to switch
            {
                "quintal" => 0.01m,
                "arroba" => 0.04m,
                "kilogramo" => 0.453592m,
                "gramo" => 453.592m,
                "tonelada" => 0.000453592m,
                "onza" => 16m,
                _ => null
            };
        }
        if (poundsPerUnit is not null && to is "kilogramo" or "kg") return poundsPerUnit * 0.453592m;
        return null;
    }

    private static string NormalizeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return string.Empty;
        var normalized = unit.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        normalized = new string(normalized.Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return normalized switch
        {
            "lb" or "lbs" or "libras" => "libra",
            "kg" or "kgs" or "kilogramos" => "kilogramo",
            "g" or "gr" or "gramos" => "gramo",
            "qq" or "quintales" => "quintal",
            "arrobas" => "arroba",
            "t" or "toneladas" => "tonelada",
            "oz" or "onzas" => "onza",
            _ => normalized
        };
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

public sealed record PurchaseProductOption(ProductId Id, string Name, string Unit)
{
    public string DisplayName => $"{Name} ({Unit})";
}

public sealed record PurchaseCartLine(ProductId ProductId, string Name, string Unit, Quantity Quantity, string ConversionText)
{
    public string QuantityText => $"{Quantity.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {Unit}";
}
