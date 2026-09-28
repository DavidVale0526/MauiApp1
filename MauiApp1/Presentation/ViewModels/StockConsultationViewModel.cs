using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Products;

namespace MauiApp1.Presentation.ViewModels;

public sealed class StockConsultationViewModel : INotifyPropertyChanged
{
    private readonly IProductService _productService;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private bool _isRefreshing;

    public StockConsultationViewModel(IProductService productService)
    {
        _productService = productService;
        RefreshCommand = new Command(async () => await LoadAsync(isRefresh: true), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProductSummary> Products { get; } = [];

    public ICommand RefreshCommand { get; }

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
                ((Command)RefreshCommand).ChangeCanExecute();
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

    public async Task LoadAsync(bool isRefresh = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsRefreshing = isRefresh;
        ErrorMessage = string.Empty;
        try
        {
            var products = await _productService.GetAllAsync();
            Products.Clear();
            foreach (var product in products)
            {
                Products.Add(product);
            }
        }
        catch
        {
            ErrorMessage = "No se pudo consultar el inventario guardado en este dispositivo. Inténtalo de nuevo.";
        }
        finally
        {
            IsRefreshing = false;
            IsBusy = false;
        }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
