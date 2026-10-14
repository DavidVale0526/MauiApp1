using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MauiApp1.Application.Categories;
using MauiApp1.Application.Customers;
using MauiApp1.Application.Products;
using MauiApp1.Application.Reports;
using MauiApp1.Domain.Inventory;

namespace MauiApp1.Presentation.ViewModels;

public enum ReportSection { Sales, Inventory, Movements }

public sealed class ReportsViewModel : INotifyPropertyChanged
{
    private readonly IReportService _reportService;
    private readonly ICustomerService _customerService;
    private readonly ICategoryService _categoryService;
    private readonly IProductService _productService;
    private ReportSection _activeSection;
    private DateTime _dateFrom = DateTime.Today;
    private DateTime _dateTo = DateTime.Today;
    private SalesModeOption? _selectedSalesMode;
    private CustomerFilterOption? _selectedCustomer;
    private CategoryFilterOption? _selectedCategory;
    private StatusFilterOption? _selectedStatus;
    private ProductFilterOption? _selectedProduct;
    private MovementTypeFilterOption? _selectedMovementType;
    private DateTime _inventoryAsOfLocal;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public ReportsViewModel(
        IReportService reportService,
        ICustomerService customerService,
        ICategoryService categoryService,
        IProductService productService)
    {
        _reportService = reportService;
        _customerService = customerService;
        _categoryService = categoryService;
        _productService = productService;
        SalesModes = [new(null, "Todas"), new(false, "Contado"), new(true, "Crédito")];
        Statuses = [new(null, "Todos"), new(true, "Activos"), new(false, "Inactivos")];
        MovementTypes =
        [
            new(null, "Todos"),
            new(InventoryMovementType.PurchaseEntry, "Entrada por compra"),
            new(InventoryMovementType.SaleExit, "Salida por venta"),
            new(InventoryMovementType.OtherExit, "Otra salida"),
            new(InventoryMovementType.MixedWaste, "Merma mixta")
        ];
        _selectedSalesMode = SalesModes[0];
        _selectedStatus = Statuses[0];
        _selectedMovementType = MovementTypes[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SalesReportCard> Sales { get; } = [];
    public ObservableCollection<InventoryReportCard> Inventory { get; } = [];
    public ObservableCollection<MovementReportCard> Movements { get; } = [];
    public ObservableCollection<SalesModeOption> SalesModes { get; }
    public ObservableCollection<CustomerFilterOption> Customers { get; } = [];
    public ObservableCollection<CategoryFilterOption> Categories { get; } = [];
    public ObservableCollection<StatusFilterOption> Statuses { get; }
    public ObservableCollection<ProductFilterOption> Products { get; } = [];
    public ObservableCollection<MovementTypeFilterOption> MovementTypes { get; }

    public ReportSection ActiveSection
    {
        get => _activeSection;
        private set
        {
            if (SetProperty(ref _activeSection, value))
            {
                OnPropertyChanged(nameof(IsSalesSection));
                OnPropertyChanged(nameof(IsInventorySection));
                OnPropertyChanged(nameof(IsMovementsSection));
            }
        }
    }

    public bool IsSalesSection => ActiveSection == ReportSection.Sales;
    public bool IsInventorySection => ActiveSection == ReportSection.Inventory;
    public bool IsMovementsSection => ActiveSection == ReportSection.Movements;
    public DateTime DateFrom { get => _dateFrom; set => SetProperty(ref _dateFrom, value.Date); }
    public DateTime DateTo { get => _dateTo; set => SetProperty(ref _dateTo, value.Date); }
    public SalesModeOption? SelectedSalesMode { get => _selectedSalesMode; set => SetProperty(ref _selectedSalesMode, value); }
    public CustomerFilterOption? SelectedCustomer { get => _selectedCustomer; set => SetProperty(ref _selectedCustomer, value); }
    public CategoryFilterOption? SelectedCategory { get => _selectedCategory; set => SetProperty(ref _selectedCategory, value); }
    public StatusFilterOption? SelectedStatus { get => _selectedStatus; set => SetProperty(ref _selectedStatus, value); }
    public ProductFilterOption? SelectedProduct { get => _selectedProduct; set => SetProperty(ref _selectedProduct, value); }
    public MovementTypeFilterOption? SelectedMovementType { get => _selectedMovementType; set => SetProperty(ref _selectedMovementType, value); }

    public DateTime InventoryAsOfLocal
    {
        get => _inventoryAsOfLocal;
        private set
        {
            if (SetProperty(ref _inventoryAsOfLocal, value)) OnPropertyChanged(nameof(InventoryAsOfText));
        }
    }

    public string InventoryAsOfText => InventoryAsOfLocal == default
        ? string.Empty
        : $"Fotografía consultada: {InventoryAsOfLocal.ToString("g", CultureInfo.CurrentCulture)}";

    public string DataNotice => "Datos locales de este dispositivo; pueden incluir operaciones aún no sincronizadas.";
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public async Task LoadAsync()
    {
        try
        {
            Customers.Clear();
            Customers.Add(new CustomerFilterOption(null, "Todos"));
            foreach (var customer in await _customerService.GetAllAsync())
                Customers.Add(new CustomerFilterOption(customer.CustomerId, customer.Name));
            SelectedCustomer = Customers[0];

            Categories.Clear();
            Categories.Add(new CategoryFilterOption(null, "Todas"));
            foreach (var category in await _categoryService.GetAllAsync())
                Categories.Add(new CategoryFilterOption(category.Id.Value, category.Name));
            SelectedCategory = Categories[0];

            Products.Clear();
            Products.Add(new ProductFilterOption(null, "Todos"));
            foreach (var product in await _productService.GetAllAsync())
                Products.Add(new ProductFilterOption(product.Id.Value, product.Name));
            SelectedProduct = Products[0];
        }
        catch
        {
            ErrorMessage = "No se pudieron cargar las opciones de los reportes.";
        }

        await RefreshAsync();
    }

    public Task ShowSalesAsync() => ChangeSectionAsync(ReportSection.Sales);
    public Task ShowInventoryAsync() => ChangeSectionAsync(ReportSection.Inventory);
    public Task ShowMovementsAsync() => ChangeSectionAsync(ReportSection.Movements);

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            switch (ActiveSection)
            {
                case ReportSection.Sales:
                    if (!TryGetUtcRange(out var salesFrom, out var salesTo)) return;
                    Sales.Clear();
                    foreach (var row in await _reportService.GetSalesAsync(
                                 salesFrom, salesTo, SelectedSalesMode?.IsCredit, SelectedCustomer?.CustomerId))
                        Sales.Add(new SalesReportCard(row));
                    break;
                case ReportSection.Inventory:
                    Inventory.Clear();
                    foreach (var row in await _reportService.GetInventoryAsync(
                                 SelectedCategory?.CategoryId, SelectedStatus?.IsActive))
                        Inventory.Add(new InventoryReportCard(row));
                    InventoryAsOfLocal = DateTime.Now;
                    break;
                case ReportSection.Movements:
                    if (!TryGetUtcRange(out var movementFrom, out var movementTo)) return;
                    Movements.Clear();
                    foreach (var row in await _reportService.GetMovementsAsync(
                                 movementFrom, movementTo, SelectedProduct?.ProductId, SelectedMovementType?.Type))
                        Movements.Add(new MovementReportCard(row));
                    break;
            }
        }
        catch
        {
            ErrorMessage = "No se pudo cargar el reporte. Inténtalo de nuevo.";
        }
        finally { IsBusy = false; }
    }

    private async Task ChangeSectionAsync(ReportSection section)
    {
        ActiveSection = section;
        await RefreshAsync();
    }

    private bool TryGetUtcRange(out DateTime fromUtc, out DateTime toUtc)
    {
        fromUtc = default;
        toUtc = default;
        if (DateTo.Date < DateFrom.Date)
        {
            ErrorMessage = "La fecha final debe ser igual o posterior a la fecha inicial.";
            return false;
        }

        try
        {
            fromUtc = DateTime.SpecifyKind(DateFrom.Date, DateTimeKind.Unspecified).ToUniversalTime();
            toUtc = DateTime.SpecifyKind(DateTo.Date.AddDays(1), DateTimeKind.Unspecified).ToUniversalTime();
            return true;
        }
        catch (ArgumentException)
        {
            ErrorMessage = "El periodo seleccionado no es válido en la zona horaria del dispositivo.";
            return false;
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

public sealed record SalesModeOption(bool? IsCredit, string Label);
public sealed record CustomerFilterOption(Guid? CustomerId, string Name);
public sealed record CategoryFilterOption(Guid? CategoryId, string Name);
public sealed record StatusFilterOption(bool? IsActive, string Label);
public sealed record ProductFilterOption(Guid? ProductId, string Name);
public sealed record MovementTypeFilterOption(InventoryMovementType? Type, string Label);

public sealed record SalesReportCard(SalesReportRow Row)
{
    public string DateText => Row.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string ReferenceText => $"Venta {Row.OperationId.ToString("N")[..8].ToUpperInvariant()}";
    public string ActorText => $"Usuario: {Row.ActorUserName}";
    public string TypeText => Row.IsCredit ? "Crédito" : "Contado";
    public string CustomerText => Row.IsCredit ? $"Cliente: {Row.CustomerName ?? "No disponible"}" : string.Empty;
    public string TotalText => $"C$ {(Row.TotalMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";
}

public sealed record InventoryReportCard(InventoryReportRow Row)
{
    public string Name => Row.ProductName;
    public string CategoryText => $"Categoría: {Row.CategoryName}";
    public string StatusText => Row.IsActive ? "Activo" : "Inactivo";
    public string StockText => $"Existencia: {(Row.StockMilliUnits / 1000m).ToString("0.###", CultureInfo.CurrentCulture)} {Row.Unit}";
    public string MinimumText => $"Mínimo: {(Row.MinimumStockMilliUnits / 1000m).ToString("0.###", CultureInfo.CurrentCulture)} {Row.Unit}";
}

public sealed record MovementReportCard(MovementReportRow Row)
{
    public string DateText => Row.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string ProductText => Row.ProductName;
    public string TypeText => Row.Type switch
    {
        InventoryMovementType.PurchaseEntry => "Entrada por compra",
        InventoryMovementType.SaleExit => "Salida por venta",
        InventoryMovementType.OtherExit => "Otra salida",
        InventoryMovementType.MixedWaste => "Merma mixta",
        _ => "Movimiento"
    };
    public string QuantityText
    {
        get
        {
            var sign = Row.Type == InventoryMovementType.PurchaseEntry ? "+" : "−";
            return $"{sign}{(Row.QuantityMilliUnits / 1000m).ToString("0.###", CultureInfo.CurrentCulture)} {Row.Unit}";
        }
    }
    public string ReasonText => string.IsNullOrWhiteSpace(Row.Reason) ? "Sin motivo adicional" : Row.Reason;
    public string ReferenceText => $"Operación {Row.OperationId.ToString("N")[..8].ToUpperInvariant()}";
    public string ActorText => $"Usuario: {Row.ActorUserName}";
}
