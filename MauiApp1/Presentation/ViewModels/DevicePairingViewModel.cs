using System.ComponentModel;
using System.Text.Json;
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
    private string _password = string.Empty, _code = string.Empty, _message = string.Empty, _generatedCode = string.Empty, _generatedQrPayload = string.Empty;
    private bool _busy, _ready, _confirmed, _hasSavedLink;

    public DevicePairingViewModel(ICoordinatorPairingClient client, IAuthenticationService authentication)
    {
        _client = client;
        _authentication = authentication;
        UserName = authentication.CurrentUser?.UserName ?? string.Empty;
        GenerateCodeCommand = new Command(async () => await GenerateCodeAsync(), () => CanGenerateCode && !IsBusy && _ready);
        PairCommand = new Command(async () => await PairAsync(), () => ShowPairForm && !IsBusy && _ready);
        UnlinkCommand = new Command(async () => await UnlinkAsync(), () => CanUnlink);
    }

    public string Address { get => _address; set => Set(ref _address, value); }
    public string Fingerprint { get => _fingerprint; set => Set(ref _fingerprint, value); }
    public string UserName { get => _userName; set => Set(ref _userName, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public string Code { get => _code; set => Set(ref _code, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string GeneratedCode { get => _generatedCode; private set => Set(ref _generatedCode, value); }
    public string GeneratedQrPayload
    {
        get => _generatedQrPayload;
        private set
        {
            if (Set(ref _generatedQrPayload, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanShowGeneratedQr)));
        }
    }
    public bool CanGenerateCode => OperatingSystem.IsWindows() && _authentication.CurrentUser?.Role == UserRole.Administrator;
    public bool CanShowGeneratedQr => CanGenerateCode && !string.IsNullOrWhiteSpace(GeneratedQrPayload);
    public bool CanScanQr => OperatingSystem.IsAndroid() && !_confirmed;
    public bool ShowPairForm => !_confirmed;
    public bool CanUnlink => _confirmed && !IsBusy;
    public bool CanEditConnection => _ready && !_hasSavedLink && !IsBusy;
    public bool CanEditAddress => _ready && !_confirmed && !IsBusy;
    public bool CanEditForm => _ready && !IsBusy;
    public string LinkStatus => _confirmed ? "Este dispositivo está vinculado." : _hasSavedLink
        ? "Vinculación pendiente. Puedes reintentar con el mismo código o ingresar uno nuevo si venció."
        : "Este dispositivo todavía no está vinculado.";
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) RefreshState(); } }
    public ICommand GenerateCodeCommand { get; }
    public ICommand PairCommand { get; }
    public ICommand UnlinkCommand { get; }

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
        Message = GeneratedCode = GeneratedQrPayload = string.Empty;
        try
        {
            var result = await _client.CreateCodeAsync(Address, Fingerprint, UserName, Password);
            Message = result.Message;
            if (result.Succeeded && result.ExpiresAtUtc.HasValue)
            {
                GeneratedCode = $"Código: {result.Code}\nVence a las {result.ExpiresAtUtc.Value.ToLocalTime():HH:mm:ss}.\nCrear otro código invalida el anterior.";
                GeneratedQrPayload = JsonSerializer.Serialize(new PairingQrData(
                    "miscelanea-ari-pairing", 1, Address.Trim(), Fingerprint.Trim(),
                    result.Code!, result.ExpiresAtUtc.Value));
            }
        }
        catch { Message = "No fue posible generar el código. Inténtalo de nuevo."; }
        finally { Password = string.Empty; IsBusy = false; }
    }

    public bool ApplyScannedQr(string rawValue)
    {
        try
        {
            var data = JsonSerializer.Deserialize<PairingQrData>(rawValue);
            if (data is null || data.Type != "miscelanea-ari-pairing" || data.Version != 1 ||
                string.IsNullOrWhiteSpace(data.Address) || string.IsNullOrWhiteSpace(data.Fingerprint) ||
                data.Code is not { Length: 8 } || !data.Code.All(char.IsAsciiDigit) ||
                data.ExpiresAtUtc <= DateTime.UtcNow)
            {
                Message = "El QR no es válido o el código ya venció. Genera uno nuevo en Windows.";
                return false;
            }

            Address = data.Address;
            Fingerprint = data.Fingerprint;
            Code = data.Code;
            Message = "Datos leídos. Pulsa «Vincular / reintentar» para confirmar este dispositivo.";
            return true;
        }
        catch (JsonException)
        {
            Message = "El QR no corresponde a una vinculación de Miscelánea Ari.";
            return false;
        }
    }

    private async Task PairAsync()
    {
        if (IsBusy || _confirmed || !_ready) return;
        IsBusy = true;
        try { Message = (await _client.PairAsync(Address, Fingerprint, Code)).Message; }
        catch { Message = "No fue posible vincular el dispositivo. Inténtalo de nuevo."; }
        finally { IsBusy = false; await LoadAsync(); }
    }

    private async Task UnlinkAsync()
    {
        if (IsBusy || !_confirmed || !_ready) return;
        IsBusy = true;
        try
        {
            var result = await _client.UnlinkAsync();
            Message = result.Message;
            if (result.Succeeded) await LoadAsync();
        }
        catch { Message = "No fue posible desvincular este dispositivo. No se borraron sus datos locales."; }
        finally { IsBusy = false; RefreshState(); }
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
        foreach (var name in new[] { nameof(ShowPairForm), nameof(CanUnlink), nameof(CanEditConnection), nameof(CanEditAddress), nameof(CanEditForm), nameof(CanScanQr), nameof(CanShowGeneratedQr), nameof(LinkStatus) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ((Command)GenerateCodeCommand).ChangeCanExecute();
        ((Command)PairCommand).ChangeCanExecute();
        ((Command)UnlinkCommand).ChangeCanExecute();
    }

    private sealed record PairingQrData(string Type, int Version, string Address, string Fingerprint,
        string Code, DateTime ExpiresAtUtc);
}
