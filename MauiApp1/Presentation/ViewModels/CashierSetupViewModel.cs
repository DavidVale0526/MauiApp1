using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Authentication;

namespace MauiApp1.Presentation.ViewModels;

public sealed class CashierSetupViewModel : INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatorCashierAccountClient _coordinatorCashierClient;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _passwordConfirmation = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public CashierSetupViewModel(IAuthenticationService authenticationService, ICoordinatorCashierAccountClient coordinatorCashierClient)
    {
        _authenticationService = authenticationService;
        _coordinatorCashierClient = coordinatorCashierClient;
        CreateAccountCommand = new Command(async () => await CreateAccountAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? AccountCreated;

    public ICommand CreateAccountCommand { get; }

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string PasswordConfirmation
    {
        get => _passwordConfirmation;
        set => SetProperty(ref _passwordConfirmation, value);
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
                ((Command)CreateAccountCommand).ChangeCanExecute();
            }
        }
    }

    private async Task CreateAccountAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        if (!string.Equals(Password, PasswordConfirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "Las contraseñas no coinciden.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = DeviceInfo.Platform == DevicePlatform.Android
                ? await _coordinatorCashierClient.CreateCashierAsync(UserName, Password)
                : await _authenticationService.CreateCashierAccountAsync(UserName, Password);
            if (result.Succeeded)
            {
                AccountCreated?.Invoke(this, EventArgs.Empty);
                return;
            }

            ErrorMessage = result.ErrorMessage ?? "No fue posible crear la cuenta.";
        }
        catch
        {
            ErrorMessage = "No fue posible crear la cuenta. Inténtalo de nuevo.";
        }
        finally
        {
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
