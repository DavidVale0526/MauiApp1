using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using MauiApp1.Application.Products;
using MauiApp1.Application.Customers;
using MauiApp1.Application.Sales;
using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Values;

namespace MauiApp1.Presentation.ViewModels;

public sealed class SaleRegistrationViewModel : INotifyPropertyChanged
{
    private readonly IProductService _productService;
    private readonly ISaleService _saleService;
    private readonly ICustomerService _customerService;
    private SaleProductOption? _selectedProduct;
    private string _quantityInput = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private SaleDraft? _pendingSale;
    private bool _pendingSaleAttempted;
    private Guid? _lastCompletedSaleOperationId;
    private bool _isCredit;
    private CreditCustomerOption? _selectedCustomer;
    private string _cashAppliedInput = "0.00";
    private string _cashReceivedInput = "0.00";
    private string _cardAppliedInput = "0.00";
    private string _transferAppliedInput = "0.00";

    public SaleRegistrationViewModel(IProductService productService, ISaleService saleService, ICustomerService customerService)
    {
        _productService = productService;
        _saleService = saleService;
        _customerService = customerService;
        Lines.CollectionChanged += OnLinesChanged;
        AddLineCommand = new Command(AddLine, () => CanEditLines && SelectedProduct is not null);
        SelectPaymentMethodCommand = new Command<string>(ApplyPaymentMethod);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SaleProductOption> Products { get; } = [];

    public ObservableCollection<SaleCartLine> Lines { get; } = [];

    public ObservableCollection<CreditCustomerOption> Customers { get; } = [];

    public ICommand AddLineCommand { get; }

    public ICommand SelectPaymentMethodCommand { get; }

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

    public bool IsCredit
    {
        get => _isCredit;
        set
        {
            if (SetProperty(ref _isCredit, value))
            {
                OnPropertyChanged(nameof(CanConfirm));
                OnPropertyChanged(nameof(IsCashSale));
                OnPropertyChanged(nameof(CanRegisterPending));
                OnPropertyChanged(nameof(PaymentErrorMessage));
                ((Command)AddLineCommand).ChangeCanExecute();
            }
        }
    }

    public CreditCustomerOption? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value)) OnPropertyChanged(nameof(CanConfirm));
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
                OnPropertyChanged(nameof(CanEditPayment));
                OnPropertyChanged(nameof(CanRegisterPending));
            }
        }
    }

    public bool CanEditLines => !IsBusy && _pendingSale is null;

    public bool CanConfirm => !IsBusy && (_pendingSale is not null || (Lines.Count > 0 && (!IsCredit || SelectedCustomer is not null)));

    public bool IsCashSale => !IsCredit;

    public bool CanEditPayment => !IsBusy && !_pendingSaleAttempted;

    public bool CanRegisterPending => !IsBusy && _pendingSale is not null &&
        (IsCredit || IsPaymentValid(out _));

    public string CashAppliedInput { get => _cashAppliedInput; set => SetPaymentInput(ref _cashAppliedInput, value); }

    public string CashReceivedInput { get => _cashReceivedInput; set => SetPaymentInput(ref _cashReceivedInput, value); }

    public string CardAppliedInput
    {
        get => _cardAppliedInput;
        set
        {
            if (SetPaymentInput(ref _cardAppliedInput, value))
            {
                RebalanceCashApplied();
            }
        }
    }

    public string TransferAppliedInput
    {
        get => _transferAppliedInput;
        set
        {
            if (SetPaymentInput(ref _transferAppliedInput, value))
            {
                RebalanceCashApplied();
            }
        }
    }

    public string PaymentErrorMessage
    {
        get => IsCashSale && !IsPaymentValid(out var error) ? error : string.Empty;
    }

    public string ChangeText
    {
        get
        {
            if (!TryGetPaymentAmounts(out var cash, out var received, out _, out _, out _) || cash <= 0)
            {
                return "Cambio: C$ 0.00";
            }

            var change = received - cash;
            return change > 0
                ? $"Cambio: C$ {(change / 100m).ToString("0.00", CultureInfo.CurrentCulture)}"
                : "Cambio: C$ 0.00";
        }
    }

    public string ConfirmButtonText => _pendingSaleAttempted ? "Reintentar la misma venta" : "Revisar y confirmar venta";

    public Guid? LastCompletedSaleOperationId => _lastCompletedSaleOperationId;

    public string CartSummaryText => $"{Lines.Count} productos";

    public string TotalAmountText => TotalText.StartsWith("Total: ", StringComparison.Ordinal)
        ? $"C$ {TotalText[7..]}"
        : TotalText;

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
        await RefreshCustomersAsync();
    }

    public async Task RefreshAfterCoordinatorChangesAsync()
    {
        if (_pendingSale is not null || IsBusy) return;
        await RefreshProductsAsync();
        await RefreshCustomersAsync();
    }

    private async Task RefreshCustomersAsync()
    {
        try
        {
            var selectedId = SelectedCustomer?.Id;
            Customers.Clear();
            foreach (var customer in await _customerService.GetAllAsync())
                Customers.Add(new CreditCustomerOption(customer.CustomerId, customer.Name));
            SelectedCustomer = Customers.FirstOrDefault(item => item.Id == selectedId);
        }
        catch
        {
            ErrorMessage = "No se pudieron cargar los clientes. Inténtalo de nuevo.";
        }
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
                    product.Id, product.Name, product.Unit, product.SalePrice, product.Stock, product.ImageData));
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

    public void ApplyPaymentMethod(string? method)
    {
        if (IsCredit || !CanEditPayment || !TryGetTotalMinorUnits(out var total))
        {
            return;
        }

        switch (method)
        {
            case "Cash":
                SetPaymentInputs(FormatMoney(total), FormatMoney(total), string.Empty, string.Empty);
                break;
            case "Card":
                SetPaymentInputs("0.00", "0.00", FormatMoney(total), string.Empty);
                break;
            case "Transfer":
                SetPaymentInputs("0.00", "0.00", string.Empty, FormatMoney(total));
                break;
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

        if (IsCredit && SelectedCustomer is null)
        {
            ErrorMessage = "Selecciona un cliente para registrar la venta a crédito.";
            return null;
        }

        var occurredAtUtc = DateTime.UtcNow;
        var totalMinorUnits = Lines.Aggregate(0L, (sum, line) => checked(sum + line.LineTotalMinorUnits));
        if (!IsCredit)
        {
            CashAppliedInput = (totalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture);
            CashReceivedInput = (totalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture);
            CardAppliedInput = string.Empty;
            TransferAppliedInput = string.Empty;
        }
        _pendingSale = new SaleDraft(
            Guid.NewGuid(),
            occurredAtUtc,
            Lines.Select(line => new SaleLineDraft(
                line.ProductId,
                line.Name,
                line.Unit,
                line.Quantity,
                line.SalePrice)).ToArray(),
            IsCredit,
            IsCredit ? SelectedCustomer!.Id : null,
            IsCredit ? occurredAtUtc.AddDays(15) : null);
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
            return $"• {line.ProductNameSnapshot}: {line.Quantity.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {line.UnitSnapshot} × {line.SalePrice.ToDecimal().ToString("0.00", CultureInfo.CurrentCulture)} = {(lineTotal / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";
        }).ToArray();
        var totalMinorUnits = _pendingSale.Lines.Aggregate(0L,
            (sum, line) => checked(sum + SaleAmountCalculator.CalculateLineTotalMinorUnits(line.SalePrice, line.Quantity)));
        var creditDetails = _pendingSale.IsCredit
            ? $"\n\nVenta a crédito para: {SelectedCustomer?.Name ?? "cliente"}\nVence: {_pendingSale.CreditDueAtUtc!.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}\nEl saldo inicial será C$ {(totalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}."
            : "\n\nVenta de contado.";
        return $"{string.Join("\n", lines)}\n\nTotal: C$ {(totalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}{creditDetails}\n\nLa fecha y hora se registrarán automáticamente.";
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

        if (!IsCredit)
        {
            if (!IsPaymentValid(out var error) || !TryBuildPayments(out var payments, out error))
            {
                return false;
            }
            _pendingSale = _pendingSale with { Payments = payments };
        }

        IsBusy = true;
        _pendingSaleAttempted = true;
        OnPropertyChanged(nameof(CanEditPayment));
        OnPropertyChanged(nameof(CanRegisterPending));
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

            var completedSale = _pendingSale;
            _lastCompletedSaleOperationId = completedSale.OperationId;
            OnPropertyChanged(nameof(LastCompletedSaleOperationId));
            Lines.Clear();
            _pendingSale = null;
            _pendingSaleAttempted = false;
            CashAppliedInput = "0.00";
            CashReceivedInput = "0.00";
            CardAppliedInput = string.Empty;
            TransferAppliedInput = string.Empty;
            OnPropertyChanged(nameof(CanEditPayment));
            StatusMessage = result.AlreadyApplied
                ? "Esta venta ya se había guardado; no se descontó la existencia otra vez."
                : completedSale.IsCredit
                    ? "Venta a crédito registrada y existencias actualizadas; la deuda quedó asociada al cliente."
                    : "Venta de contado guardada y existencias actualizadas.";
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

        if (!TryParseQuantity(QuantityInput, out var amount))
        {
            ErrorMessage = "Escribe una cantidad válida. Puedes usar punto o coma decimal, por ejemplo 0.75.";
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
                ErrorMessage = $"Existencia insuficiente para {SelectedProduct.Name}. Disponible: {SelectedProduct.Stock.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {SelectedProduct.Unit}.";
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

    private static bool TryParseQuantity(string? input, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        // Acepta punto o coma decimal, sin interpretar separadores de miles ambiguos.
        var normalized = input.Trim().Replace(',', '.');
        if (normalized.Count(character => character == '.') > 1) return false;

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    private bool SetPaymentInput(ref string storage, string value)
    {
        if (!SetProperty(ref storage, value ?? string.Empty))
        {
            return false;
        }

        OnPropertyChanged(nameof(CanRegisterPending));
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(PaymentErrorMessage));
        return true;
    }

    private void RebalanceCashApplied()
    {
        if (!TryGetTotalMinorUnits(out var total) ||
            !TryParseMoneyOrZero(CardAppliedInput, out var card) ||
            !TryParseMoneyOrZero(TransferAppliedInput, out var transfer))
        {
            return;
        }

        var newCash = card >= total || transfer >= total ? 0 : total - card - transfer;
        if (!TryParseMoneyOrZero(CashAppliedInput, out var previousCash))
        {
            previousCash = 0;
        }

        if (!TryParseMoneyOrZero(CashReceivedInput, out var received))
        {
            received = 0;
        }

        var newCashText = FormatMoney(newCash);
        if (CashAppliedInput != newCashText)
        {
            _cashAppliedInput = newCashText;
            OnPropertyChanged(nameof(CashAppliedInput));
        }

        // El efectivo recibido acompaña al importe asignado a efectivo mientras no se haya
        // capturado un monto distinto; si no queda efectivo aplicado, no puede haber recibido.
        var newReceived = newCash == 0 ? 0 : received == previousCash ? newCash : received;
        var newReceivedText = FormatMoney(newReceived);
        if (CashReceivedInput != newReceivedText)
        {
            _cashReceivedInput = newReceivedText;
            OnPropertyChanged(nameof(CashReceivedInput));
        }

        OnPropertyChanged(nameof(CanRegisterPending));
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(PaymentErrorMessage));
    }

    private void SetPaymentInputs(string cash, string received, string card, string transfer)
    {
        _cashAppliedInput = cash;
        _cashReceivedInput = received;
        _cardAppliedInput = card;
        _transferAppliedInput = transfer;
        OnPropertyChanged(nameof(CashAppliedInput));
        OnPropertyChanged(nameof(CashReceivedInput));
        OnPropertyChanged(nameof(CardAppliedInput));
        OnPropertyChanged(nameof(TransferAppliedInput));
        OnPropertyChanged(nameof(CanRegisterPending));
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(PaymentErrorMessage));
    }

    private static string FormatMoney(long minorUnits) =>
        (minorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture);

    private bool TryGetTotalMinorUnits(out long total)
    {
        try
        {
            total = Lines.Aggregate(0L, (sum, line) => checked(sum + line.LineTotalMinorUnits));
            return true;
        }
        catch (OverflowException)
        {
            total = 0;
            return false;
        }
    }

    private bool IsPaymentValid(out string error)
    {
        error = string.Empty;
        if (!TryGetPaymentAmounts(out var cash, out var received, out var card, out var transfer, out var total))
        {
            error = "Escribe importes válidos con máximo dos decimales.";
            return false;
        }
        if (cash < 0 || received < 0 || card < 0 || transfer < 0)
        {
            error = "Los importes no pueden ser negativos.";
            return false;
        }
        var applied = cash + card + transfer;
        if (card > total)
        {
            error = $"El importe de tarjeta (C$ {(card / 100m).ToString("0.00", CultureInfo.CurrentCulture)}) supera el total de la venta (C$ {(total / 100m).ToString("0.00", CultureInfo.CurrentCulture)}).";
            return false;
        }
        if (transfer > total)
        {
            error = $"El importe de transferencia (C$ {(transfer / 100m).ToString("0.00", CultureInfo.CurrentCulture)}) supera el total de la venta (C$ {(total / 100m).ToString("0.00", CultureInfo.CurrentCulture)}).";
            return false;
        }
        if (applied != total)
        {
            var appliedText = (applied / 100m).ToString("0.00", CultureInfo.CurrentCulture);
            var totalText = (total / 100m).ToString("0.00", CultureInfo.CurrentCulture);
            error = applied > total
                ? $"Entre tarjeta y transferencia aplicaste C$ {((card + transfer) / 100m).ToString("0.00", CultureInfo.CurrentCulture)} y el total es C$ {totalText}. Reduce C$ {((applied - total) / 100m).ToString("0.00", CultureInfo.CurrentCulture)}."
                : $"La suma aplicada (C$ {appliedText}) no cubre el total (C$ {totalText}). Faltan C$ {((total - applied) / 100m).ToString("0.00", CultureInfo.CurrentCulture)}.";
            return false;
        }
        if (cash > 0 && received < cash)
        {
            error = "El efectivo recibido no cubre el importe asignado a efectivo.";
            return false;
        }
        if (cash == 0 && received != 0)
        {
            error = "Indica efectivo recibido solo cuando parte de la venta se pague en efectivo.";
            return false;
        }
        return true;
    }

    private bool TryBuildPayments(out IReadOnlyList<SalePaymentDraft> payments, out string error)
    {
        payments = [];
        if (!TryGetPaymentAmounts(out var cash, out var received, out var card, out var transfer, out _))
        {
            error = "Escribe importes válidos con máximo dos decimales.";
            return false;
        }
        var items = new List<SalePaymentDraft>(3);
        if (cash > 0) items.Add(new SalePaymentDraft(Guid.NewGuid(), SalePaymentMethod.Cash, cash, received, received - cash));
        if (card > 0) items.Add(new SalePaymentDraft(Guid.NewGuid(), SalePaymentMethod.Card, card));
        if (transfer > 0) items.Add(new SalePaymentDraft(Guid.NewGuid(), SalePaymentMethod.Transfer, transfer));
        payments = items;
        error = string.Empty;
        return true;
    }

    private bool TryGetPaymentAmounts(out long cash, out long received, out long card, out long transfer, out long total)
    {
        cash = received = card = transfer = total = 0;
        if (!TryParseMoneyOrZero(CashAppliedInput, out cash) || !TryParseMoneyOrZero(CashReceivedInput, out received) ||
            !TryParseMoneyOrZero(CardAppliedInput, out card) || !TryParseMoneyOrZero(TransferAppliedInput, out transfer))
            return false;
        try
        {
            total = Lines.Aggregate(0L, (sum, line) => checked(sum + line.LineTotalMinorUnits));
            _ = checked(cash + card + transfer);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseMoneyOrZero(string? input, out long minorUnits)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            minorUnits = 0;
            return true;
        }

        return TryParseMoney(input, out minorUnits);
    }

    private static bool TryParseMoney(string? input, out long minorUnits)
    {
        minorUnits = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var normalized = input.Trim().Replace(',', '.');
        if (normalized.Count(character => character == '.') > 1 ||
            !decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ||
            amount < 0 || decimal.Round(amount, 2) != amount)
            return false;
        try
        {
            minorUnits = checked((long)(amount * 100m));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(TotalAmountText));
        OnPropertyChanged(nameof(CartSummaryText));
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

public sealed record CreditCustomerOption(Guid Id, string Name)
{
    public string DisplayName => Name;
}

public sealed record SaleProductOption(ProductId Id, string Name, string Unit, MoneyAmount SalePrice, Quantity Stock,
    byte[]? ImageData = null)
{
    public string DisplayName => $"{Name} ({Unit})";

    public bool HasImage => ImageData is { Length: > 0 };

    public ImageSource? ProductImageSource => HasImage
        ? ImageSource.FromStream(() => new MemoryStream(ImageData!))
        : null;

    public string PriceText => $"C$ {SalePrice.ToDecimal().ToString("0.00", CultureInfo.CurrentCulture)}";

    public string StockText => $"Disponible: {Stock.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {Unit}";

    public string PriceAndStockText =>
        $"Precio: {SalePrice.ToDecimal().ToString("0.00", CultureInfo.CurrentCulture)}   |   Disponible: {Stock.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {Unit}";
}

public sealed record SaleCartLine(ProductId ProductId, string Name, string Unit, Quantity Quantity, MoneyAmount SalePrice)
{
    public long LineTotalMinorUnits => SaleAmountCalculator.CalculateLineTotalMinorUnits(SalePrice, Quantity);

    public string QuantityText => $"{Quantity.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture)} {Unit}";

    public string PriceText => $"C$ {SalePrice.ToDecimal().ToString("0.00", CultureInfo.CurrentCulture)} c/u";

    public string TotalAmountText => $"C$ {(LineTotalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";

    public string TotalText => $"Subtotal: {(LineTotalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";
}
