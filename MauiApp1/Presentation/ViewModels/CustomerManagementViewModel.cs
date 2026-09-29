using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Customers;

namespace MauiApp1.Presentation.ViewModels;

public sealed class CustomerManagementViewModel : INotifyPropertyChanged
{
    private readonly ICustomerService _customerService;
    private string _name = string.Empty;
    private string _phone = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public CustomerManagementViewModel(ICustomerService customerService)
    {
        _customerService = customerService;
        SaveCommand = new Command(async () => await SaveAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CustomerListItem> Customers { get; } = [];

    public ICommand SaveCommand { get; }

    public string Name { get => _name; set => SetProperty(ref _name, value); }

    public string Phone { get => _phone; set => SetProperty(ref _phone, value); }

    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) ((Command)SaveCommand).ChangeCanExecute(); }
    }

    public async Task LoadAsync()
    {
        try
        {
            Customers.Clear();
            foreach (var customer in await _customerService.GetAllAsync())
                Customers.Add(new CustomerListItem(customer));
        }
        catch
        {
            ErrorMessage = "No se pudieron cargar los clientes. Inténtalo de nuevo.";
        }
    }

    private async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _customerService.CreateAsync(Name, Phone);
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo guardar el cliente.";
                return;
            }

            Name = string.Empty;
            Phone = string.Empty;
            await LoadAsync();
        }
        catch
        {
            ErrorMessage = "No se pudo guardar el cliente. Inténtalo de nuevo.";
        }
        finally { IsBusy = false; }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed record CustomerListItem(CustomerSummary Customer)
{
    public string Name => Customer.Name;
    public string ContactText => string.IsNullOrWhiteSpace(Customer.Phone) ? "Sin teléfono" : Customer.Phone;
    public string DebtText => $"Saldo pendiente: C$ {(Customer.OutstandingMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";
}
