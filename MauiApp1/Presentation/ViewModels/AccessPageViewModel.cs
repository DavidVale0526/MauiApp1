using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Authentication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Extensions.Logging;

namespace MauiApp1.Presentation.ViewModels;

public sealed class AccessPageViewModel : INotifyPropertyChanged
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;
    private readonly IRecoveryCodeGenerator _recoveryCodeGenerator;
    private readonly ILogger<AccessPageViewModel> _logger;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _passwordConfirmation = string.Empty;
    private string _errorMessage = string.Empty;
    private string _noticeMessage = string.Empty;
    private string _recoveryCode = string.Empty;
    private string _recoveryUserName = string.Empty;
    private string _recoveryCodeInput = string.Empty;
    private string _newPassword = string.Empty;
    private string _newPasswordConfirmation = string.Empty;
    private bool _isSetupMode;
    private bool _isInitialRecoveryCodeStage;
    private bool _isRecoveryMode;
    private bool _isRecoveryCodeResultStage;
    private bool _isBusy;

    public AccessPageViewModel(
        IAuthenticationService authenticationService,
        ICoordinatedSignInService coordinatedSignInService,
        IRecoveryCodeGenerator recoveryCodeGenerator,
        ILogger<AccessPageViewModel> logger)
    {
        _authenticationService = authenticationService;
        _coordinatedSignInService = coordinatedSignInService;
        _recoveryCodeGenerator = recoveryCodeGenerator;
        _logger = logger;
        CreateAdministratorCommand = new Command(PrepareAdministratorCreation, () => !IsBusy);
        ConfirmAdministratorSetupCommand = new Command(async () => await ConfirmAdministratorCreationAsync(), () => !IsBusy);
        CancelAdministratorSetupCommand = new Command(CancelAdministratorCreation, () => !IsBusy);
        SignInCommand = new Command(async () => await SignInAsync(), () => !IsBusy);
        OpenPasswordRecoveryCommand = new Command(OpenPasswordRecovery, () => !IsBusy);
        ResetPasswordCommand = new Command(async () => await ResetPasswordAsync(), () => !IsBusy);
        BackToLoginCommand = new Command(BackToLogin, () => !IsBusy);
        CopyRecoveryCodeCommand = new Command(async () => await CopyRecoveryCodeAsync(), () => !IsBusy);
        AcknowledgeRecoveryCodeCommand = new Command(AcknowledgeRecoveryCode, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CreateAdministratorCommand { get; }

    public ICommand SignInCommand { get; }
    public ICommand ConfirmAdministratorSetupCommand { get; }
    public ICommand CancelAdministratorSetupCommand { get; }
    public ICommand OpenPasswordRecoveryCommand { get; }
    public ICommand ResetPasswordCommand { get; }
    public ICommand BackToLoginCommand { get; }
    public ICommand CopyRecoveryCodeCommand { get; }
    public ICommand AcknowledgeRecoveryCodeCommand { get; }

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
                OnPropertyChanged(nameof(IsLoginForm));
                OnPropertyChanged(nameof(IsInitialSetupForm));
                OnPropertyChanged(nameof(IsCredentialFormVisible));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool IsLoginMode => !IsSetupMode;

    public bool IsInitialRecoveryCodeStage
    {
        get => _isInitialRecoveryCodeStage;
        private set
        {
            if (SetProperty(ref _isInitialRecoveryCodeStage, value))
            {
                OnPropertyChanged(nameof(IsInitialSetupForm));
                OnPropertyChanged(nameof(IsCredentialFormVisible));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool IsRecoveryMode
    {
        get => _isRecoveryMode;
        private set
        {
            if (SetProperty(ref _isRecoveryMode, value))
            {
                OnPropertyChanged(nameof(IsLoginForm));
                OnPropertyChanged(nameof(IsRecoveryForm));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool IsRecoveryCodeResultStage
    {
        get => _isRecoveryCodeResultStage;
        private set
        {
            if (SetProperty(ref _isRecoveryCodeResultStage, value))
            {
                OnPropertyChanged(nameof(IsRecoveryForm));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool IsLoginForm => IsLoginMode && !IsRecoveryMode;
    public bool IsInitialSetupForm => IsSetupMode && !IsInitialRecoveryCodeStage;
    public bool IsCredentialFormVisible => IsLoginForm || IsInitialSetupForm;
    public bool IsRecoveryForm => IsRecoveryMode && !IsRecoveryCodeResultStage;

    public string Title => IsInitialRecoveryCodeStage
        ? "Guarda el código de recuperación"
        : IsRecoveryCodeResultStage
            ? "Guarda tu nuevo código"
            : IsRecoveryMode
                ? "Recuperar contraseña"
                : IsSetupMode ? "Configurar cuenta de Administrador" : "Iniciar sesión";

    public string RecoveryCode
    {
        get => _recoveryCode;
        private set => SetProperty(ref _recoveryCode, value);
    }

    public string RecoveryUserName { get => _recoveryUserName; set => SetProperty(ref _recoveryUserName, value); }
    public string RecoveryCodeInput { get => _recoveryCodeInput; set => SetProperty(ref _recoveryCodeInput, value); }
    public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }
    public string NewPasswordConfirmation { get => _newPasswordConfirmation; set => SetProperty(ref _newPasswordConfirmation, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((Command)CreateAdministratorCommand).ChangeCanExecute();
                ((Command)ConfirmAdministratorSetupCommand).ChangeCanExecute();
                ((Command)CancelAdministratorSetupCommand).ChangeCanExecute();
                ((Command)SignInCommand).ChangeCanExecute();
                ((Command)OpenPasswordRecoveryCommand).ChangeCanExecute();
                ((Command)ResetPasswordCommand).ChangeCanExecute();
                ((Command)BackToLoginCommand).ChangeCanExecute();
                ((Command)CopyRecoveryCodeCommand).ChangeCanExecute();
                ((Command)AcknowledgeRecoveryCodeCommand).ChangeCanExecute();
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

    private void PrepareAdministratorCreation()
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(UserName) || UserName.Trim().Length > 80)
        {
            ErrorMessage = "Escribe un nombre de usuario de hasta 80 caracteres.";
            return;
        }
        if (Password.Length is < 5 or > 1024)
        {
            ErrorMessage = "La contraseña debe tener entre 5 y 1024 caracteres.";
            return;
        }
        if (!string.Equals(Password, PasswordConfirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "Las contraseñas no coinciden.";
            return;
        }

        RecoveryCode = _recoveryCodeGenerator.Generate();
        IsInitialRecoveryCodeStage = true;
    }

    private async Task ConfirmAdministratorCreationAsync()
    {
        if (IsBusy || !IsInitialRecoveryCodeStage) return;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _authenticationService.CreateInitialAdministratorAsync(UserName, Password, RecoveryCode);
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No fue posible crear la cuenta.";
                return;
            }

            Password = string.Empty;
            PasswordConfirmation = string.Empty;
            RecoveryCode = string.Empty;
            IsInitialRecoveryCodeStage = false;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "No se pudo crear la cuenta inicial de Administrador.");
            ErrorMessage = "No fue posible crear la cuenta. El código sigue visible; vuelve a intentarlo.";
        }
        finally { IsBusy = false; }
    }

    private void CancelAdministratorCreation()
    {
        RecoveryCode = string.Empty;
        IsInitialRecoveryCodeStage = false;
        ErrorMessage = string.Empty;
    }

    private void OpenPasswordRecovery()
    {
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        RecoveryUserName = UserName;
        RecoveryCodeInput = string.Empty;
        NewPassword = string.Empty;
        NewPasswordConfirmation = string.Empty;
        RecoveryCode = string.Empty;
        IsRecoveryCodeResultStage = false;
        IsRecoveryMode = true;
    }

    private async Task ResetPasswordAsync()
    {
        if (IsBusy) return;
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(RecoveryUserName) || RecoveryUserName.Trim().Length > 80)
        {
            ErrorMessage = "Escribe el nombre de usuario.";
            return;
        }
        if (NewPassword.Length is < 5 or > 1024)
        {
            ErrorMessage = "La nueva contraseña debe tener entre 5 y 1024 caracteres.";
            return;
        }
        if (!string.Equals(NewPassword, NewPasswordConfirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "Las contraseñas no coinciden.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _coordinatedSignInService.ResetPasswordAsync(
                RecoveryUserName, RecoveryCodeInput, NewPassword);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.RecoveryCode))
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo restablecer la contraseña.";
                return;
            }

            RecoveryCode = result.RecoveryCode;
            RecoveryCodeInput = string.Empty;
            NewPassword = string.Empty;
            NewPasswordConfirmation = string.Empty;
            IsRecoveryCodeResultStage = true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "No se pudo completar el restablecimiento de contraseña.");
            ErrorMessage = "No se pudo confirmar el cambio. Conéctate a la red del negocio y vuelve a intentarlo.";
        }
        finally { IsBusy = false; }
    }

    private void BackToLogin()
    {
        if (IsRecoveryCodeResultStage) UserName = RecoveryUserName;
        IsRecoveryCodeResultStage = false;
        IsRecoveryMode = false;
        RecoveryCode = string.Empty;
        RecoveryCodeInput = string.Empty;
        NewPassword = string.Empty;
        NewPasswordConfirmation = string.Empty;
        Password = string.Empty;
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
    }

    private async Task CopyRecoveryCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(RecoveryCode)) return;
        await Clipboard.Default.SetTextAsync(RecoveryCode);
        NoticeMessage = "Código copiado. Guárdalo en un lugar seguro.";
    }

    private void AcknowledgeRecoveryCode()
    {
        BackToLogin();
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
