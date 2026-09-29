using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Lan;
using MauiApp1.Domain.Users;

namespace MauiApp1.Presentation.ViewModels;

public sealed class DevicePairingViewModel : INotifyPropertyChanged
{
    private readonly ICoordinatorPairingClient _client;
    private readonly IAuthenticationService _authentication;
    private string _address = string.Empty, _fingerprint = string.Empty, _userName = string.Empty;
    private string _password = string.Empty, _code = string.Empty, _message = string.Empty, _generatedCode = string.Empty;
    private bool _busy, _ready, _confirmed, _hasSavedLink;

    public DevicePairingViewModel(ICoordinatorPairingClient client, IAuthenticationService authentication)
    {
        _client = client;
        _authentication = authentication;
        UserName = authentication.CurrentUser?.UserName ?? string.Empty;
        GenerateCodeCommand = new Command(async () => await GenerateCodeAsync(), () => CanGenerateCode && !IsBusy && _ready);
        PairCommand = new Command(async () => await PairAsync(), () => ShowPairForm && !IsBusy && _ready);
    }

    public string Address { get => _address; set => Set(ref _address, value); }
    public string Fingerprint { get => _fingerprint; set => Set(ref _fingerprint, value); }
    public string UserName { get => _userName; set => Set(ref _userName, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public string Code { get => _code; set => Set(ref _code, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string GeneratedCode { get => _generatedCode; private set => Set(ref _generatedCode, value); }
    public bool CanGenerateCode => OperatingSystem.IsWindows() && _authentication.CurrentUser?.Role == UserRole.Administrator;
    public bool ShowPairForm => !_confirmed;
    public bool CanEditConnection => _ready && !_hasSavedLink && !IsBusy;
    public bool CanEditAddress => _ready && !_confirmed && !IsBusy;
    public bool CanEditForm => _ready && !IsBusy;
    public string LinkStatus => _confirmed ? "Este dispositivo está vinculado." : _hasSavedLink
        ? "Vinculación pendiente. Puedes reintentar con el mismo código o ingresar uno nuevo si venció."
        : "Este dispositivo todavía no está vinculado.";
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) RefreshState(); } }
    public ICommand GenerateCodeCommand { get; }
    public ICommand PairCommand { get; }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var status = await _client.GetStatusAsync();
            _hasSavedLink = status is not null;
            _confirmed = status?.IsConfirmed == true;
            if (status is not null)
            {
                Address = status.Address;
                Fingerprint = status.CertificateFingerprint;
                Code = status.PendingCode ?? string.Empty;
            }
            _ready = true;
        }
        catch
        {
            _ready = false;
            Message = "No se pudo leer la autorización segura del dispositivo. No se reemplazará automáticamente.";
        }
        finally { IsBusy = false; RefreshState(); }
    }

    private async Task GenerateCodeAsync()
    {
        if (IsBusy || !CanGenerateCode || !_ready) return;
        IsBusy = true;
        Message = GeneratedCode = string.Empty;
        try
        {
            var result = await _client.CreateCodeAsync(Address, Fingerprint, UserName, Password);
            Message = result.Message;
            if (result.Succeeded && result.ExpiresAtUtc.HasValue)
                GeneratedCode = $"Código: {result.Code}\nVence a las {result.ExpiresAtUtc.Value.ToLocalTime():HH:mm:ss}.\nCrear otro código invalida el anterior.";
        }
        catch { Message = "No fue posible generar el código. Inténtalo de nuevo."; }
        finally { Password = string.Empty; IsBusy = false; }
    }

    private async Task PairAsync()
    {
        if (IsBusy || _confirmed || !_ready) return;
        IsBusy = true;
        try { Message = (await _client.PairAsync(Address, Fingerprint, Code)).Message; }
        catch { Message = "No fue posible vincular el dispositivo. Inténtalo de nuevo."; }
        finally { IsBusy = false; await LoadAsync(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    private void RefreshState()
    {
        foreach (var name in new[] { nameof(ShowPairForm), nameof(CanEditConnection), nameof(CanEditAddress), nameof(CanEditForm), nameof(LinkStatus) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ((Command)GenerateCodeCommand).ChangeCanExecute();
        ((Command)PairCommand).ChangeCanExecute();
    }
}
