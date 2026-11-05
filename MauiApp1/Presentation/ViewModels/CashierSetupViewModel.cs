using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Configuration;
using MauiApp1.Domain.Users;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace MauiApp1.Presentation.ViewModels;

public sealed class CashierSetupViewModel : INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatorUserAccountClient _coordinatorUserClient;
    private readonly ICoordinatorUserSessionStorage _coordinatorSessionStorage;
    private readonly DesktopCoordinatorModeOptions _desktopMode;
    private readonly IRecoveryCodeGenerator _recoveryCodeGenerator;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _passwordConfirmation = string.Empty;
    private string _selectedRole = CashierRoleLabel;
    private string _errorMessage = string.Empty;
    private string _successMessage = string.Empty;
    private string _recoveryCode = string.Empty;
    private string _listErrorMessage = string.Empty;
    private bool _isBusy;

    private const string CashierRoleLabel = "Cajero";
    private const string AdministratorRoleLabel = "Administrador";

    public CashierSetupViewModel(
        IAuthenticationService authenticationService,
        ICoordinatorUserAccountClient coordinatorUserClient,
        ICoordinatorUserSessionStorage coordinatorSessionStorage,
        DesktopCoordinatorModeOptions desktopMode,
        IRecoveryCodeGenerator recoveryCodeGenerator)
    {
        _authenticationService = authenticationService;
        _coordinatorUserClient = coordinatorUserClient;
        _coordinatorSessionStorage = coordinatorSessionStorage;
        _desktopMode = desktopMode;
        _recoveryCodeGenerator = recoveryCodeGenerator;
        CreateAccountCommand = new Command(async () => await CreateAccountAsync(), () => !IsBusy);
        RefreshCashiersCommand = new Command(async () => await LoadCashiersAsync(), () => !IsBusy);
        CopyRecoveryCodeCommand = new Command(async () => await Clipboard.Default.SetTextAsync(RecoveryCode));
        DismissRecoveryCodeCommand = new Command(() => RecoveryCode = string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CreateAccountCommand { get; }
    public ICommand RefreshCashiersCommand { get; }
    public ICommand CopyRecoveryCodeCommand { get; }
    public ICommand DismissRecoveryCodeCommand { get; }
    public ObservableCollection<CashierAccountRow> Cashiers { get; } = new();

    public IReadOnlyList<string> RoleOptions { get; } = [CashierRoleLabel, AdministratorRoleLabel];

    public string SelectedRole
    {
        get => _selectedRole;
        set => SetProperty(ref _selectedRole, value);
    }

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

    public string RecoveryCode
    {
        get => _recoveryCode;
        private set
        {
            if (SetProperty(ref _recoveryCode, value)) OnPropertyChanged(nameof(HasRecoveryCode));
        }
    }

    public bool HasRecoveryCode => !string.IsNullOrWhiteSpace(RecoveryCode);

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
    public string CashierCountText => Cashiers.Count == 1 ? "1 cuenta registrada" : $"{Cashiers.Count} cuentas registradas";

    public async Task LoadCashiersAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            await RefreshAccountsCoreAsync();
        }
        catch (UnauthorizedAccessException)
        {
            ListErrorMessage = "Solo el Administrador puede consultar las cuentas.";
        }
        catch
        {
            ListErrorMessage = "No se pudo cargar la lista de cuentas. Inténtalo de nuevo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Cambia el rol de una cuenta existente. La decisión se guarda en el
    /// coordinador (o en la base activa si este equipo es el host).
    /// </summary>
    public async Task<(bool Succeeded, string Message)> ChangeRoleAsync(CashierAccountRow row)
    {
        if (IsBusy)
            return (false, "Espera a que termine la operación actual.");

        var newRole = row.Role == UserRole.Administrator ? UserRole.Cashier : UserRole.Administrator;
        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;
        try
        {
            var useCoordinator = await ShouldUseCoordinatorAccountStoreAsync();
            var result = useCoordinator
                ? await _coordinatorUserClient.ChangeRoleAsync(row.UserId, newRole)
                : await _authenticationService.ChangeRoleAsync(row.UserId, newRole);

            if (!result.Succeeded)
            {
                var error = result.ErrorMessage ?? "No se pudo cambiar el rol.";
                ErrorMessage = error;
                return (false, error);
            }

            var message = newRole == UserRole.Administrator
                ? $"La cuenta «{row.UserName}» ahora es Administrador."
                : $"La cuenta «{row.UserName}» ahora es Cajero.";
            if (result.SessionRevoked)
                message += " Se cerraron sus sesiones; deberá volver a iniciar sesión.";
            SuccessMessage = message;
            await RefreshAccountsCoreAsync();
            return (true, message);
        }
        catch
        {
            const string error = "No se pudo cambiar el rol. Comprueba la conexión e inténtalo de nuevo.";
            ErrorMessage = error;
            return (false, error);
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
        RecoveryCode = string.Empty;
        if (!string.Equals(Password, PasswordConfirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "Las contraseñas no coinciden.";
            return;
        }

        IsBusy = true;
        try
        {
            var role = SelectedRole == AdministratorRoleLabel ? UserRole.Administrator : UserRole.Cashier;
            var useCoordinator = await ShouldUseCoordinatorAccountStoreAsync();
            var recoveryCode = _recoveryCodeGenerator.Generate();
            var result = useCoordinator
                ? await _coordinatorUserClient.CreateAccountAsync(UserName, Password, recoveryCode, role)
                : await _authenticationService.CreateAccountAsync(UserName, Password, recoveryCode, role);

            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No fue posible crear la cuenta.";
                if (result.OutcomeUnknown && result.RecoveryCode is not null)
                {
                    RecoveryCode = result.RecoveryCode;
                    SuccessMessage = "No se recibió confirmación. Conserva el código y verifica si la cuenta aparece en la lista.";
                    Password = string.Empty;
                    PasswordConfirmation = string.Empty;
                    await RefreshAccountsCoreAsync();
                }
                return;
            }

            UserName = string.Empty;
            Password = string.Empty;
            PasswordConfirmation = string.Empty;
            SelectedRole = CashierRoleLabel;
            RecoveryCode = result.RecoveryCode ?? recoveryCode;
            SuccessMessage = role == UserRole.Administrator
                ? "Cuenta de Administrador creada. Entrega este código y guárdalo en un lugar seguro."
                : "Cuenta creada. Entrega este código a la persona que usará la cuenta y guárdalo en un lugar seguro.";
            await RefreshAccountsCoreAsync();
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

    private async Task RefreshAccountsCoreAsync()
    {
        ListErrorMessage = string.Empty;
        IReadOnlyList<UserAccountSummary> accounts;
        try
        {
            var useCoordinator = await ShouldUseCoordinatorAccountStoreAsync();
            if (useCoordinator)
            {
                var result = await _coordinatorUserClient.GetAccountsAsync();
                if (!result.Succeeded)
                {
                    ListErrorMessage = result.ErrorMessage ?? "No se pudo consultar la lista central de cuentas.";
                    return;
                }

                accounts = result.Accounts;
            }
            else
            {
                accounts = await _authenticationService.GetAccountsAsync();
            }
        }
        catch (UnauthorizedAccessException)
        {
            ListErrorMessage = "Solo el Administrador puede consultar las cuentas.";
            return;
        }
        catch
        {
            ListErrorMessage = "No se pudo cargar la lista de cuentas. Inténtalo de nuevo.";
            return;
        }

        var currentUserId = _authenticationService.CurrentUser?.Id.Value;
        Cashiers.Clear();
        foreach (var account in accounts
                     .Where(account => !string.IsNullOrWhiteSpace(account.UserName) && account.UserName.Length <= 80)
                     .OrderByDescending(account => account.Role)
                     .ThenBy(account => account.UserName, StringComparer.CurrentCultureIgnoreCase))
        {
            Cashiers.Add(new CashierAccountRow(
                account.UserId,
                account.UserName,
                account.Role,
                account.IsEnabled,
                account.CreatedAtUtc,
                isCurrentUser: account.UserId == currentUserId));
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

    public CashierAccountRow(
        Guid userId,
        string userName,
        UserRole role,
        bool isEnabled,
        DateTime createdAtUtc,
        bool isCurrentUser)
    {
        UserId = userId;
        UserName = userName;
        Role = role;
        IsEnabled = isEnabled;
        IsCurrentUser = isCurrentUser;
        CreatedAtText = createdAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", SpanishCulture);
    }

    public Guid UserId { get; }
    public string UserName { get; }
    public UserRole Role { get; }
    public bool IsEnabled { get; }
    public bool IsCurrentUser { get; }
    public bool IsAdministrator => Role == UserRole.Administrator;
    public string RoleText => IsAdministrator ? "Administrador" : "Cajero";
    public Microsoft.Maui.Graphics.Color RoleColor => IsAdministrator
        ? Microsoft.Maui.Graphics.Color.FromArgb("#0D5C4A")
        : Microsoft.Maui.Graphics.Color.FromArgb("#52657D");
    public string StatusText => IsEnabled ? "Activo" : "Deshabilitado";
    public Microsoft.Maui.Graphics.Color StatusTextColor => IsEnabled
        ? Microsoft.Maui.Graphics.Color.FromArgb("#008060")
        : Microsoft.Maui.Graphics.Color.FromArgb("#B42318");
    public string CreatedAtText { get; }
    public string DetailsText => $"{StatusText} · Agregado el {CreatedAtText}";
    public bool CanChangeRole => !IsCurrentUser;
    public string ToggleRoleText => IsAdministrator ? "Hacer cajero" : "Hacer administrador";
}
