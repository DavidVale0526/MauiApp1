using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Configuration;
using MauiApp1.Domain.Users;

namespace MauiApp1.Presentation.ViewModels;

public sealed class CashierSetupViewModel : INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatorCashierAccountClient _coordinatorCashierClient;
    private readonly ICoordinatorUserSessionStorage _coordinatorSessionStorage;
    private readonly DesktopCoordinatorModeOptions _desktopMode;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _passwordConfirmation = string.Empty;
    private string _errorMessage = string.Empty;
    private string _successMessage = string.Empty;
    private string _listErrorMessage = string.Empty;
    private bool _isBusy;

    public CashierSetupViewModel(
        IAuthenticationService authenticationService,
        ICoordinatorCashierAccountClient coordinatorCashierClient,
        ICoordinatorUserSessionStorage coordinatorSessionStorage,
        DesktopCoordinatorModeOptions desktopMode)
    {
        _authenticationService = authenticationService;
        _coordinatorCashierClient = coordinatorCashierClient;
        _coordinatorSessionStorage = coordinatorSessionStorage;
        _desktopMode = desktopMode;
        CreateAccountCommand = new Command(async () => await CreateAccountAsync(), () => !IsBusy);
        RefreshCashiersCommand = new Command(async () => await LoadCashiersAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CreateAccountCommand { get; }
    public ICommand RefreshCashiersCommand { get; }
    public ObservableCollection<CashierAccountRow> Cashiers { get; } = new();

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

    public string SuccessMessage
    {
        get => _successMessage;
        private set => SetProperty(ref _successMessage, value);
    }

    public string ListErrorMessage
    {
        get => _listErrorMessage;
        private set
        {
            if (SetProperty(ref _listErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasListError));
                OnPropertyChanged(nameof(ShowEmptyMessage));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((Command)CreateAccountCommand).ChangeCanExecute();
                ((Command)RefreshCashiersCommand).ChangeCanExecute();
                OnPropertyChanged(nameof(ShowEmptyMessage));
            }
        }
    }

    public bool HasCashiers => Cashiers.Count > 0;
    public bool HasListError => !string.IsNullOrWhiteSpace(ListErrorMessage);
    public bool ShowEmptyMessage => !IsBusy && !HasCashiers && string.IsNullOrWhiteSpace(ListErrorMessage);
    public string CashierCountText => Cashiers.Count == 1 ? "1 cajero registrado" : $"{Cashiers.Count} cajeros registrados";

    public async Task LoadCashiersAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            await RefreshCashiersCoreAsync();
        }
        catch (UnauthorizedAccessException)
        {
            ListErrorMessage = "Solo el Administrador puede consultar las cuentas de cajero.";
        }
        catch
        {
            ListErrorMessage = "No se pudo cargar la lista de cajeros. Inténtalo de nuevo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateAccountAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;
        if (!string.Equals(Password, PasswordConfirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "Las contraseñas no coinciden.";
            return;
        }

        IsBusy = true;
        try
        {
            var useCoordinator = await ShouldUseCoordinatorAccountStoreAsync();
            var result = useCoordinator
                ? await _coordinatorCashierClient.CreateCashierAsync(UserName, Password)
                : await _authenticationService.CreateCashierAccountAsync(UserName, Password);

            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No fue posible crear la cuenta.";
                return;
            }

            UserName = string.Empty;
            Password = string.Empty;
            PasswordConfirmation = string.Empty;
            SuccessMessage = "Cuenta de cajero creada correctamente.";
            await RefreshCashiersCoreAsync();
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

    private async Task RefreshCashiersCoreAsync()
    {
        ListErrorMessage = string.Empty;
        IReadOnlyList<CashierAccountSummary> accounts;
        try
        {
            var useCoordinator = await ShouldUseCoordinatorAccountStoreAsync();
            if (useCoordinator)
            {
                var result = await _coordinatorCashierClient.GetCashiersAsync();
                if (!result.Succeeded)
                {
                    ListErrorMessage = result.ErrorMessage ?? "No se pudo consultar la lista central de cajeros.";
                    return;
                }

                accounts = result.Accounts;
            }
            else
            {
                accounts = await _authenticationService.GetCashierAccountsAsync();
            }
        }
        catch (UnauthorizedAccessException)
        {
            ListErrorMessage = "Solo el Administrador puede consultar las cuentas de cajero.";
            return;
        }
        catch
        {
            ListErrorMessage = "No se pudo cargar la lista de cajeros. Inténtalo de nuevo.";
            return;
        }

        Cashiers.Clear();
        foreach (var account in accounts
                     .Where(account => !string.IsNullOrWhiteSpace(account.UserName) && account.UserName.Length <= 80)
                     .OrderBy(account => account.UserName, StringComparer.CurrentCultureIgnoreCase))
        {
            Cashiers.Add(new CashierAccountRow(
                account.UserName,
                account.IsEnabled,
                account.CreatedAtUtc));
        }

        OnPropertyChanged(nameof(HasCashiers));
        OnPropertyChanged(nameof(ShowEmptyMessage));
        OnPropertyChanged(nameof(CashierCountText));
    }

    private async Task<bool> ShouldUseCoordinatorAccountStoreAsync()
    {
        if (OperatingSystem.IsAndroid())
            return true;

        if (_desktopMode.IsHost)
            return false;

        var currentUser = _authenticationService.CurrentUser;
        if (currentUser is null || currentUser.Role != UserRole.Administrator)
            return false;

        var session = await _coordinatorSessionStorage.ReadAsync();
        return session is not null &&
            session.Role == UserRole.Administrator &&
            session.LocalUserId == currentUser.Id.Value &&
            session.ExpiresAtUtc > DateTime.UtcNow;
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class CashierAccountRow
{
    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-MX");

    public CashierAccountRow(string userName, bool isEnabled, DateTime createdAtUtc)
    {
        UserName = userName;
        IsEnabled = isEnabled;
        CreatedAtText = createdAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", SpanishCulture);
    }

    public string UserName { get; }
    public bool IsEnabled { get; }
    public string StatusText => IsEnabled ? "Activo" : "Deshabilitado";
    public Microsoft.Maui.Graphics.Color StatusTextColor => IsEnabled
        ? Microsoft.Maui.Graphics.Color.FromArgb("#008060")
        : Microsoft.Maui.Graphics.Color.FromArgb("#B42318");
    public string CreatedAtText { get; }
    public string CreatedAtDescription => $"Agregado el {CreatedAtText}";
}
