using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using MauiApp1.Application.Categories;
using MauiApp1.Application.Products;
using MauiApp1.Domain.Values;

namespace MauiApp1.Presentation.ViewModels;

public sealed class ProductManagementViewModel : INotifyPropertyChanged
{
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private string _name = string.Empty;
    private string _salePrice = string.Empty;
    private string _unit = string.Empty;
    private string _minimumStock = "0";
    private byte[]? _imageData;
    private bool _replaceImage;
    private string _errorMessage = string.Empty;
    private CategorySummary? _selectedCategory;
    private Guid? _editingProductId;
    private bool _isBusy;

    public ProductManagementViewModel(IProductService productService, ICategoryService categoryService)
    {
        _productService = productService;
        _categoryService = categoryService;
        SaveCommand = new Command(async () => await SaveAsync(), () => !IsBusy);
        CancelEditCommand = new Command(CancelEdit, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProductSummary> Products { get; } = [];

    public ObservableCollection<CategorySummary> Categories { get; } = [];

    public ICommand SaveCommand { get; }

    public ICommand CancelEditCommand { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string SalePrice
    {
        get => _salePrice;
        set => SetProperty(ref _salePrice, value);
    }

    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value);
    }

    public string MinimumStock
    {
        get => _minimumStock;
        set => SetProperty(ref _minimumStock, value);
    }

    public bool HasProductImage => _imageData is { Length: > 0 };

    public ImageSource? ProductImagePreview => _imageData is { Length: > 0 } image
        ? ImageSource.FromStream(() => new MemoryStream(image))
        : null;

    public string ProductImageStatus => HasProductImage
        ? "Imagen seleccionada. Se mostrará en el catálogo de ventas."
        : "Sin imagen. Puedes agregar una imagen JPG o PNG de hasta 512 KB.";

    public void SetErrorMessage(string message) => ErrorMessage = message;

    public void SetProductImage(byte[] imageData)
    {
        _imageData = imageData;
        _replaceImage = true;
        OnPropertyChanged(nameof(HasProductImage));
        OnPropertyChanged(nameof(ProductImagePreview));
        OnPropertyChanged(nameof(ProductImageStatus));
        ErrorMessage = string.Empty;
    }

    public void RemoveProductImage()
    {
        _imageData = null;
        _replaceImage = true;
        OnPropertyChanged(nameof(HasProductImage));
        OnPropertyChanged(nameof(ProductImagePreview));
        OnPropertyChanged(nameof(ProductImageStatus));
        ErrorMessage = string.Empty;
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public CategorySummary? SelectedCategory
    {
        get => _selectedCategory;
        set => SetProperty(ref _selectedCategory, value);
    }

    public bool IsEditing => _editingProductId.HasValue;

    public string SaveButtonText => IsEditing ? "Guardar cambios" : "Crear producto";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((Command)SaveCommand).ChangeCanExecute();
                ((Command)CancelEditCommand).ChangeCanExecute();
            }
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var categories = await _categoryService.GetAllAsync();
            var products = await _productService.GetAllAsync();

            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }

            Products.Clear();
            foreach (var product in products)
            {
                Products.Add(product);
            }

            if (_editingProductId is null && SelectedCategory is null)
            {
                SelectedCategory = Categories.FirstOrDefault();
            }

            ErrorMessage = categories.Count == 0
                ? "Primero crea una categoría para poder registrar productos."
                : string.Empty;
        }
        catch
        {
            ErrorMessage = "No se pudieron cargar los productos y categorías. Inténtalo de nuevo.";
        }
    }

    public void BeginEdit(ProductSummary product)
    {
        _editingProductId = product.Id.Value;
        Name = product.Name;
        SalePrice = product.SalePrice.ToDecimal().ToString("0.00", CultureInfo.CurrentCulture);
        Unit = product.Unit;
        MinimumStock = product.MinimumStock.ToDecimal().ToString("0.###", CultureInfo.CurrentCulture);
        _imageData = product.ImageData;
        _replaceImage = false;
        OnPropertyChanged(nameof(HasProductImage));
        OnPropertyChanged(nameof(ProductImagePreview));
        OnPropertyChanged(nameof(ProductImageStatus));
        SelectedCategory = Categories.FirstOrDefault(category => category.Id.Value == product.CategoryId.Value);
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    public async Task SetActiveAsync(ProductSummary product, bool isActive)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _productService.SetActiveAsync(product.Id.Value, isActive);
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo cambiar el estado del producto.";
                return;
            }

            if (_editingProductId == product.Id.Value)
            {
                CancelEdit();
            }

            await LoadAsync();
        }
        catch
        {
            ErrorMessage = "No se pudo cambiar el estado del producto. Inténtalo de nuevo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        if (!decimal.TryParse(SalePrice, NumberStyles.Number, CultureInfo.CurrentCulture, out var salePrice))
        {
            ErrorMessage = "Escribe un precio válido usando el separador decimal de este dispositivo.";
            return;
        }

        if (!decimal.TryParse(MinimumStock, NumberStyles.Number, CultureInfo.CurrentCulture, out var minimumStock))
        {
            ErrorMessage = "Escribe un nivel mínimo válido usando el separador decimal de este dispositivo.";
            return;
        }

        if (SelectedCategory is null)
        {
            ErrorMessage = "Selecciona una categoría.";
            return;
        }

        ProductDraft draft;
        try
        {
            draft = new ProductDraft(
                Name,
                MoneyAmount.FromDecimal(salePrice),
                Unit,
                SelectedCategory.Id,
                Quantity.FromDecimal(minimumStock),
                _imageData,
                _replaceImage);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ErrorMessage = exception.ParamName == "value"
                ? "El precio admite hasta 2 decimales y el nivel mínimo hasta 3."
                : "Revisa el precio y el nivel mínimo.";
            return;
        }
        catch (OverflowException)
        {
            ErrorMessage = "El precio o el nivel mínimo supera el valor permitido.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = _editingProductId is Guid productId
                ? await _productService.UpdateAsync(productId, draft)
                : await _productService.CreateAsync(draft);

            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo guardar el producto.";
                return;
            }

            CancelEdit();
            await LoadAsync();
        }
        catch
        {
            ErrorMessage = "No se pudo guardar el producto. Inténtalo de nuevo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelEdit()
    {
        _editingProductId = null;
        Name = string.Empty;
        SalePrice = string.Empty;
        Unit = string.Empty;
        MinimumStock = "0";
        _imageData = null;
        _replaceImage = false;
        SelectedCategory = Categories.FirstOrDefault();
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasProductImage));
        OnPropertyChanged(nameof(ProductImagePreview));
        OnPropertyChanged(nameof(ProductImageStatus));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
