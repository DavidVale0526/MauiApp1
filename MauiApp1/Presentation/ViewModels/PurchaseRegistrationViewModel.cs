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

    public ObservableCollection<PurchaseCartLine> Lines { get; } = [];

    public ICommand AddLineCommand { get; }

    public PurchaseProductOption? SelectedProduct
    {
        get => _selectedProduct;
        set => SetProperty(ref _selectedProduct, value);
    }

    public string QuantityInput
    {
        get => _quantity;
        set => SetProperty(ref _quantity, value);
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
        try
        {
            var products = await _productService.GetAllAsync();
            Products.Clear();
            foreach (var product in products.Where(item => item.IsActive))
            {
                Products.Add(new PurchaseProductOption(product.Id, product.Name, product.Unit));
            }

            SelectedProduct ??= Products.FirstOrDefault();
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

        var lines = Lines.Select(line => $"• {line.Name}: {line.Quantity.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {line.Unit}");
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
            if (existing is null)
            {
                Lines.Add(new PurchaseCartLine(SelectedProduct.Id, SelectedProduct.Name, SelectedProduct.Unit, parsed));
            }
            else
            {
                var total = checked(existing.Quantity.MilliUnits + parsed.MilliUnits);
                var index = Lines.IndexOf(existing);
                Lines[index] = existing with { Quantity = new Quantity(total) };
            }
        }
        catch (OverflowException)
        {
            ErrorMessage = "La cantidad acumulada de este producto supera el valor permitido.";
            return;
        }

        QuantityInput = string.Empty;
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

public sealed record PurchaseCartLine(ProductId ProductId, string Name, string Unit, Quantity Quantity)
{
    public string QuantityText => $"{Quantity.ToDecimal().ToString("0.000", CultureInfo.CurrentCulture)} {Unit}";
}
