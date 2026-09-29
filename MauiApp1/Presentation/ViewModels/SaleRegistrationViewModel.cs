using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Products;
using MauiApp1.Application.Sales;
using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Values;

namespace MauiApp1.Presentation.ViewModels;

public sealed class SaleRegistrationViewModel : INotifyPropertyChanged
{
    private readonly IProductService _productService;
    private readonly ISaleService _saleService;
    private SaleProductOption? _selectedProduct;
    private string _quantityInput = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private SaleDraft? _pendingSale;
    private bool _pendingSaleAttempted;
    private Guid? _lastCompletedSaleOperationId;

    public SaleRegistrationViewModel(IProductService productService, ISaleService saleService)
    {
        _productService = productService;
        _saleService = saleService;
        Lines.CollectionChanged += OnLinesChanged;
        AddLineCommand = new Command(AddLine, () => CanEditLines && SelectedProduct is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SaleProductOption> Products { get; } = [];

    public ObservableCollection<SaleCartLine> Lines { get; } = [];

    public ICommand AddLineCommand { get; }

    public SaleProductOption? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetProperty(ref _selectedProduct, value))
            {
                OnPropertyChanged(nameof(SelectedProductDetails));
                ((Command)AddLineCommand).ChangeCanExecute();
            }
        }
    }

    public string SelectedProductDetails => SelectedProduct?.PriceAndStockText ?? "Selecciona un producto para ver su precio y existencia.";

    public string QuantityInput
    {
        get => _quantityInput;
        set => SetProperty(ref _quantityInput, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
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
                OnPropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public bool CanEditLines => !IsBusy && _pendingSale is null;

    public bool CanConfirm => !IsBusy && (_pendingSale is not null || Lines.Count > 0);

    public string ConfirmButtonText => _pendingSaleAttempted ? "Reintentar la misma venta" : "Revisar y confirmar venta";

    public Guid? LastCompletedSaleOperationId => _lastCompletedSaleOperationId;

    public string TotalText
    {
        get
        {
            try
            {
                var totalMinorUnits = Lines.Aggregate(0L, (sum, line) => checked(sum + line.LineTotalMinorUnits));
                return $"Total: {(totalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";
            }
            catch (OverflowException)
            {
                return "Total: supera el valor permitido";
            }
        }
    }

    public async Task LoadAsync()
    {
        if (_pendingSale is not null)
        {
            return;
        }

        await RefreshProductsAsync();
    }

    private async Task RefreshProductsAsync()
    {
        var selectedProductId = SelectedProduct?.Id;
        try
        {
            var products = await _productService.GetAllAsync();
            Products.Clear();
            foreach (var product in products.Where(item => item.IsActive))
            {
                Products.Add(new SaleProductOption(
                    product.Id, product.Name, product.Unit, product.SalePrice, product.Stock));
            }

            SelectedProduct = Products.FirstOrDefault(product => product.Id == selectedProductId) ?? Products.FirstOrDefault();
            ErrorMessage = Products.Count == 0 ? "No hay productos activos para vender." : string.Empty;
        }
        catch
        {
            ErrorMessage = "No se pudieron cargar los productos activos. Inténtalo de nuevo.";
        }
    }

    public void RemoveLine(SaleCartLine? line)
    {
        if (CanEditLines && line is not null)
        {
            Lines.Remove(line);
            ErrorMessage = string.Empty;
        }
    }

    public SaleDraft? PrepareSale()
    {
        if (_pendingSale is not null)
        {
            return _pendingSale;
        }

        if (Lines.Count == 0)
        {
            ErrorMessage = "Agrega al menos un producto antes de revisar la venta.";
            return null;
        }

        _pendingSale = new SaleDraft(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Lines.Select(line => new SaleLineDraft(
                line.ProductId,
                line.Name,
                line.Unit,
                line.Quantity,
                line.SalePrice)).ToArray());
        _pendingSaleAttempted = false;
        OnPropertyChanged(nameof(CanEditLines));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(ConfirmButtonText));
        ((Command)AddLineCommand).ChangeCanExecute();
        return _pendingSale;
    }

    public string BuildReviewText()
    {
        if (_pendingSale is null)
        {
            return string.Empty;
        }

        var lines = _pendingSale.Lines.Select(line =>
        {
            var lineTotal = SaleAmountCalculator.CalculateLineTotalMinorUnits(line.SalePrice, line.Quantity);
            return $"• {line.ProductNameSnapshot}: {line.Quantity.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {line.UnitSnapshot} × {line.SalePrice.ToDecimal().ToString("0.00", CultureInfo.CurrentCulture)} = {(lineTotal / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";
        }).ToArray();
        var totalMinorUnits = _pendingSale.Lines.Aggregate(0L,
            (sum, line) => checked(sum + SaleAmountCalculator.CalculateLineTotalMinorUnits(line.SalePrice, line.Quantity)));
        return $"{string.Join("\n", lines)}\n\n{(totalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}\n\nLa fecha y hora se registrarán automáticamente. Esta etapa registra la venta y actualiza existencias; no registra cobros ni emite comprobantes.";
    }

    public void CancelReview()
    {
        if (_pendingSaleAttempted || _pendingSale is null)
        {
            return;
        }

        _pendingSale = null;
        OnPropertyChanged(nameof(CanEditLines));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(ConfirmButtonText));
        ((Command)AddLineCommand).ChangeCanExecute();
    }

    public async Task<bool> RecordPendingAsync()
    {
        if (IsBusy || _pendingSale is null)
        {
            return false;
        }

        IsBusy = true;
        _pendingSaleAttempted = true;
        OnPropertyChanged(nameof(ConfirmButtonText));
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var result = await _saleService.RecordAsync(_pendingSale);
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo guardar la venta.";
                return false;
            }

            _lastCompletedSaleOperationId = _pendingSale.OperationId;
            OnPropertyChanged(nameof(LastCompletedSaleOperationId));
            Lines.Clear();
            _pendingSale = null;
            _pendingSaleAttempted = false;
            StatusMessage = result.AlreadyApplied
                ? "Esta venta ya se había guardado; no se descontó la existencia otra vez."
                : "Venta guardada y existencias actualizadas. El cobro y el comprobante se definirán en sus etapas correspondientes.";
            await RefreshProductsAsync();
            OnPropertyChanged(nameof(CanEditLines));
            OnPropertyChanged(nameof(CanConfirm));
            OnPropertyChanged(nameof(ConfirmButtonText));
            ((Command)AddLineCommand).ChangeCanExecute();
            return true;
        }
        catch
        {
            ErrorMessage = "No se pudo confirmar la venta. Reintenta con el mismo contenido para evitar duplicados.";
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
        StatusMessage = string.Empty;
        if (SelectedProduct is null)
        {
            ErrorMessage = "Selecciona un producto activo.";
            return;
        }

        if (!decimal.TryParse(QuantityInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
        {
            ErrorMessage = "Escribe una cantidad válida usando el separador decimal de este dispositivo.";
            return;
        }

        Quantity parsed;
        try
        {
            parsed = Quantity.FromDecimal(amount);
            if (parsed.MilliUnits <= 0)
            {
                ErrorMessage = "La cantidad debe ser mayor que cero.";
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

        var existing = Lines.FirstOrDefault(line => line.ProductId == SelectedProduct.Id);
        try
        {
            var totalQuantity = checked((existing?.Quantity.MilliUnits ?? 0) + parsed.MilliUnits);
            if (totalQuantity > SelectedProduct.Stock.MilliUnits)
            {
                ErrorMessage = $"Existencia insuficiente para {SelectedProduct.Name}. Disponible: {SelectedProduct.Stock.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {SelectedProduct.Unit}.";
                return;
            }

            var newLine = new SaleCartLine(
                SelectedProduct.Id,
                SelectedProduct.Name,
                SelectedProduct.Unit,
                new Quantity(totalQuantity),
                SelectedProduct.SalePrice);
            _ = newLine.LineTotalMinorUnits;
            if (existing is null)
            {
                Lines.Add(newLine);
            }
            else
            {
                Lines[Lines.IndexOf(existing)] = newLine;
            }
        }
        catch (OverflowException)
        {
            ErrorMessage = "La cantidad o el importe supera el valor permitido.";
            return;
        }
        catch (ArgumentOutOfRangeException)
        {
            ErrorMessage = "La cantidad o el precio del producto no es válido.";
            return;
        }

        QuantityInput = string.Empty;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(CanConfirm));
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

public sealed record SaleProductOption(ProductId Id, string Name, string Unit, MoneyAmount SalePrice, Quantity Stock)
{
    public string DisplayName => $"{Name} ({Unit})";

    public string PriceAndStockText =>
        $"Precio: {SalePrice.ToDecimal().ToString("0.00", CultureInfo.CurrentCulture)}   |   Disponible: {Stock.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {Unit}";
}

public sealed record SaleCartLine(ProductId ProductId, string Name, string Unit, Quantity Quantity, MoneyAmount SalePrice)
{
    public long LineTotalMinorUnits => SaleAmountCalculator.CalculateLineTotalMinorUnits(SalePrice, Quantity);

    public string QuantityText => $"{Quantity.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {Unit}";

    public string PriceText => $"{SalePrice.ToDecimal().ToString("0.00", CultureInfo.CurrentCulture)} c/u";

    public string TotalText => $"Subtotal: {(LineTotalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";
}
