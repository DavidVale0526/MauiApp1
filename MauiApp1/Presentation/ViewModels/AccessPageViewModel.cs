using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Authentication;
using Microsoft.Extensions.Logging;

namespace MauiApp1.Presentation.ViewModels;

public sealed class AccessPageViewModel : INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;
    private readonly ILogger<AccessPageViewModel> _logger;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _passwordConfirmation = string.Empty;
    private string _errorMessage = string.Empty;
    private string _noticeMessage = string.Empty;
    private bool _isSetupMode;
    private bool _isBusy;

    public AccessPageViewModel(
        IAuthenticationService authenticationService,
        ICoordinatedSignInService coordinatedSignInService,
        ILogger<AccessPageViewModel> logger)
    {
        _authenticationService = authenticationService;
        _coordinatedSignInService = coordinatedSignInService;
        _logger = logger;
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
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string NoticeMessage
    {
        get => _noticeMessage;
        private set
        {
            if (SetProperty(ref _noticeMessage, value)) OnPropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeMessage);

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
            NoticeMessage = string.Empty;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "No se pudo preparar la pantalla de acceso.");
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
        NoticeMessage = string.Empty;
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
        NoticeMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _coordinatedSignInService.SignInAsync(UserName, Password);
            if (!result.Authentication.Succeeded)
            {
                _logger.LogInformation("El flujo de acceso no autenticó al usuario.");
                ErrorMessage = result.Authentication.ErrorMessage ?? "No fue posible completar el acceso.";
                NoticeMessage = result.Notice ?? string.Empty;
                Password = string.Empty;
                return;
            }

            NoticeMessage = result.Notice ?? string.Empty;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falló el flujo coordinado de inicio de sesión para {UserName}.", UserName);
            ErrorMessage = "No fue posible completar el acceso. Inténtalo de nuevo.";
            Password = string.Empty;
        }
        finally { IsBusy = false; }
    }

    private async Task RunAuthenticationAsync(Func<Task<AuthenticationResult>> operation)
    {
        IsBusy = true;
        try
        {
            var result = await operation();
            if (!result.Succeeded)
            {
                _logger.LogInformation("La operación de acceso terminó sin autenticar al usuario.");
                ErrorMessage = result.ErrorMessage ?? "No fue posible completar la operación.";
                Password = string.Empty;
                PasswordConfirmation = string.Empty;
                return;
            }

            _logger.LogDebug("La operación de acceso fue aceptada por el servicio.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falló la operación de autenticación para el usuario {UserName}.", UserName);
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
