using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Authentication;

namespace MauiApp1.Presentation.ViewModels;

public sealed class AccessPageViewModel : INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _passwordConfirmation = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isSetupMode;
    private bool _isBusy;

    public AccessPageViewModel(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
        CreateAdministratorCommand = new Command(async () => await CreateAdministratorAsync(), () => !IsBusy);
        SignInCommand = new Command(async () => await SignInAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CreateAdministratorCommand { get; }

    public ICommand SignInCommand { get; }

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

    public bool IsSetupMode
    {
        get => _isSetupMode;
        private set
        {
            if (SetProperty(ref _isSetupMode, value))
            {
                OnPropertyChanged(nameof(IsLoginMode));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool IsLoginMode => !IsSetupMode;

    public string Title => IsSetupMode ? "Configurar cuenta de Administrador" : "Iniciar sesión";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((Command)CreateAdministratorCommand).ChangeCanExecute();
                ((Command)SignInCommand).ChangeCanExecute();
            }
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsSetupMode = !await _authenticationService.HasAdministratorAsync();
            ErrorMessage = string.Empty;
        }
        catch
        {
            ErrorMessage = "No fue posible preparar el acceso. Cierra y vuelve a abrir la aplicación.";
        }
    }

    private async Task CreateAdministratorAsync()
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

        await RunAuthenticationAsync(
            () => _authenticationService.CreateInitialAdministratorAsync(UserName, Password));
    }

    private async Task SignInAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        await RunAuthenticationAsync(() => _authenticationService.SignInAsync(UserName, Password));
    }

    private async Task RunAuthenticationAsync(Func<Task<AuthenticationResult>> operation)
    {
        IsBusy = true;
        try
        {
            var result = await operation();
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No fue posible completar la operación.";
                Password = string.Empty;
                PasswordConfirmation = string.Empty;
            }
        }
        catch
        {
            ErrorMessage = "No fue posible completar la operación. Inténtalo de nuevo.";
            Password = string.Empty;
            PasswordConfirmation = string.Empty;
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
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
