using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Lan;
using MauiApp1.Configuration;
using MauiApp1.Domain.Users;
using MauiApp1.Services;
using Microsoft.Maui.Storage;

namespace MauiApp1.Presentation.ViewModels;

public sealed class DevicePairingViewModel : INotifyPropertyChanged
{
    private readonly ICoordinatorPairingClient _client;
    private readonly ICoordinatorDiscovery _discovery;
    private readonly ICoordinatorDiscoveryPairingClient _discoveryPairing;
    private readonly ICoordinatorDiscoveryApprovalClient _discoveryApproval;
    private readonly IAuthenticationService _authentication;
    private readonly DesktopCoordinatorModeOptions _desktopMode;
    private readonly ICoordinatorHostEnvironment _hostEnvironment;
    public bool IsWindowsHost => _hostEnvironment.IsWindows && _desktopMode.IsHost;
    private bool _hostAddressValidated;
    private string _validatedHostAddress = string.Empty, _validatedHostCandidates = string.Empty;
    private string _hostAddressMessage = string.Empty;
    public string HostAddressMessage { get => _hostAddressMessage; private set => Set(ref _hostAddressMessage, value); }
    public bool ShowHostAddressConfirmation => IsWindowsHost && !_hostAddressValidated;
    private string _address = string.Empty, _fingerprint = string.Empty, _userName = string.Empty;
    private string _password = string.Empty, _code = string.Empty, _message = string.Empty, _generatedCode = string.Empty, _generatedQrPayload = string.Empty;
    private bool _busy, _ready, _confirmed, _hasSavedLink, _hasPendingLink;
    private CoordinatorCandidate? _selectedCoordinator;
    private Guid? _discoveryRequestId;
    private string _discoveryVerificationCode = string.Empty, _discoveryRequestStatus = string.Empty;
    private DateTime? _discoveryRequestExpiry;
    private string? _adminApprovalToken;
    private DateTime _adminApprovalExpiry;
    private CancellationTokenSource? _discoveryPollingCancellation;
    private CancellationTokenSource? _coordinatorSearchCancellation;

    public DevicePairingViewModel(
        ICoordinatorPairingClient client,
        ICoordinatorDiscovery discovery,
        ICoordinatorDiscoveryPairingClient discoveryPairing,
        ICoordinatorDiscoveryApprovalClient discoveryApproval,
        IAuthenticationService authentication,
        DesktopCoordinatorModeOptions desktopMode,
        ICoordinatorHostEnvironment? hostEnvironment = null)
    {
        _client = client;
        _discovery = discovery;
        _discoveryPairing = discoveryPairing;
        _discoveryApproval = discoveryApproval;
        _authentication = authentication;
        _desktopMode = desktopMode;
        _hostEnvironment = hostEnvironment ?? new CoordinatorHostEnvironment();
        UserName = authentication.CurrentUser?.UserName ?? string.Empty;
        GenerateCodeCommand = new Command(async () => await GenerateCodeAsync(), () => CanGenerateCode && !IsBusy && _ready && (!IsWindowsHost || _hostAddressValidated));
        ConfirmHostAddressCommand = new Command(ConfirmHostAddress, () => IsWindowsHost && _ready && !IsBusy);
        LoadFingerprintCommand = new Command(async () => await LoadFingerprintAsync(), () => CanLoadFingerprintFromFile && CanEditConnection && !IsBusy);
        PairCommand = new Command(async () => await PairAsync(), () => ShowPairForm && !IsBusy && _ready);
        DiscoverCommand = new Command(async () => await DiscoverCoordinatorAsync(), () => CanDiscover);
        RefreshApprovalRequestsCommand = new Command(async () => await RefreshApprovalRequestsAsync(), () => CanManageDiscoveredPairings && !IsBusy);
        CancelDiscoveredPairingCommand = new Command(async () => await CancelDiscoveredPairingAsync(), () => CanCancelDiscoveredPairing && !IsBusy);
    }

    public string Address
    {
        get => _address;
        set
        {
            if (Set(ref _address, value) && IsWindowsHost && _hostAddressValidated &&
                !string.Equals(value, _validatedHostAddress, StringComparison.Ordinal))
                InvalidateHostAddress("Confirma la dirección LAN antes de generar un código.");
        }
    }
    private string _newCoordinatorAddress = string.Empty, _addressUpdateMessage = string.Empty;
    public string NewCoordinatorAddress { get => _newCoordinatorAddress; set => Set(ref _newCoordinatorAddress, value); }
    public string AddressUpdateMessage { get => _addressUpdateMessage; private set => Set(ref _addressUpdateMessage, value); }
    public bool ShowAddressUpdate => !IsWindowsHost && _ready && _confirmed;
    public bool CanUpdateCoordinatorAddress => ShowAddressUpdate && !IsBusy;
    public string Fingerprint { get => _fingerprint; set => Set(ref _fingerprint, value); }
    public string UserName { get => _userName; set => Set(ref _userName, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public string Code { get => _code; set => Set(ref _code, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string GeneratedCode { get => _generatedCode; private set => Set(ref _generatedCode, value); }
    public ObservableCollection<CoordinatorCandidate> DiscoveredCoordinators { get; } = [];
    public ObservableCollection<CoordinatorPendingPairing> PendingDiscoveredPairings { get; } = [];
    public CoordinatorCandidate? SelectedCoordinator
    {
        get => _selectedCoordinator;
        private set
        {
            if (Set(ref _selectedCoordinator, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRequestDiscoveredPairing)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedCoordinator)));
            }
        }
    }
    public string DiscoveryVerificationCode { get => _discoveryVerificationCode; private set => Set(ref _discoveryVerificationCode, value); }
    public string DiscoveryRequestStatus { get => _discoveryRequestStatus; private set => Set(ref _discoveryRequestStatus, value); }
    public DateTime? DiscoveryRequestExpiry { get => _discoveryRequestExpiry; private set => Set(ref _discoveryRequestExpiry, value); }
    public string GeneratedQrPayload
    {
        get => _generatedQrPayload;
        private set
        {
            if (Set(ref _generatedQrPayload, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanShowGeneratedQr)));
        }
    }
    public bool CanGenerateCode => _hostEnvironment.IsWindows && _authentication.CurrentUser?.Role == UserRole.Administrator;
    public bool CanLoadFingerprintFromFile => _hostEnvironment.IsWindows && !IsWindowsHost;
    public bool CanShowGeneratedQr => CanGenerateCode && !string.IsNullOrWhiteSpace(GeneratedQrPayload);
    public bool CanScanQr => _hostEnvironment.IsAndroid && !_confirmed;
    public bool CanDiscover => !_desktopMode.IsHost && !_confirmed && (!_hasSavedLink || _hasPendingLink) &&
        !_discoveryRequestId.HasValue && !IsBusy;
    public bool HasDiscoveredCoordinators => DiscoveredCoordinators.Count > 0;
    public bool HasSelectedCoordinator => SelectedCoordinator is not null;
    public bool CanRequestDiscoveredPairing => SelectedCoordinator is not null && !_discoveryRequestId.HasValue &&
        !_confirmed && (!_hasSavedLink || _hasPendingLink) && !IsBusy;
    public bool ShowDiscoveryRequest => _discoveryRequestId.HasValue;
    public bool CanCancelDiscoveredPairing => _discoveryRequestId.HasValue && !IsBusy;
    public bool CanManageDiscoveredPairings => IsWindowsHost && CanGenerateCode;
    public bool ShowPairForm => !IsWindowsHost && !_confirmed;
    public bool CanUnlink => !IsWindowsHost && _confirmed && !IsBusy;
    public bool CanEditConnection => !IsWindowsHost && _ready && !_hasSavedLink && !IsBusy;
    public bool CanEditAddress => _ready && (IsWindowsHost || !_confirmed) && !IsBusy;
    public bool CanEditForm => _ready && !IsBusy;
    public string LinkStatus => IsWindowsHost ? "Este Windows es el Coordinador local. Autoriza otros dispositivos; no necesita vincularse consigo mismo."
        : _confirmed ? "Este dispositivo está vinculado." : _hasSavedLink
        ? "Vinculación pendiente. Puedes reintentar con el mismo código o ingresar uno nuevo si venció."
        : "Este dispositivo todavía no está vinculado.";
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) RefreshState(); } }
    public ICommand GenerateCodeCommand { get; }
    public ICommand ConfirmHostAddressCommand { get; }
    public ICommand LoadFingerprintCommand { get; }
    public ICommand PairCommand { get; }
    public ICommand DiscoverCommand { get; }
    public ICommand RefreshApprovalRequestsCommand { get; }
    public ICommand CancelDiscoveredPairingCommand { get; }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            if (IsWindowsHost)
            {
                _ready = false;
                _hasSavedLink = _confirmed = _hasPendingLink = _hostAddressValidated = false;
                Message = Code = GeneratedCode = GeneratedQrPayload = string.Empty;
                Address = Fingerprint = string.Empty;
                Fingerprint = await ReadHostFingerprintAsync();
                _ready = true;
                var candidates = HostCandidates();
                if (candidates.Count == 1)
                {
                    Address = candidates[0].Address;
                    ValidateHostAddress(candidates);
                }
                else InvalidateHostAddress(DescribeCandidates(candidates));
                return;
            }
            var status = await _client.GetStatusAsync();
            _hasSavedLink = status is not null;
            _confirmed = status?.IsConfirmed == true;
            _hasPendingLink = status is { IsConfirmed: false };
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
            if (IsWindowsHost) InvalidateHostAddress("No se pudo comprobar la red LAN o el certificado instalado del servicio Coordinador. Revisa su configuración y vuelve a abrir esta pantalla.");
            else Message = "No se pudo leer la autorización segura del dispositivo. No se reemplazará automáticamente.";
        }
        finally { IsBusy = false; RefreshState(); }
    }

    private async Task GenerateCodeAsync()
    {
        if (IsBusy || !CanGenerateCode || !_ready || IsWindowsHost && !_hostAddressValidated) return;
        IsBusy = true;
        Message = GeneratedCode = GeneratedQrPayload = string.Empty;
        try
        {
            if (IsWindowsHost && !await CheckHostAddressAsync()) return;
            var address = Address;
            var fingerprint = Fingerprint;
            var result = await _client.CreateCodeAsync(address, fingerprint, UserName, Password);
            if (IsWindowsHost && !await CheckHostAddressAsync()) return;
            if (IsWindowsHost && (Address != address || Fingerprint != fingerprint))
            {
                InvalidateHostAddress("Los datos del Coordinador cambiaron durante la solicitud. Confirma la dirección y genera otro código.");
                Message = HostAddressMessage;
                return;
            }
            Message = result.Message;
            if (result.Succeeded && result.ExpiresAtUtc.HasValue)
            {
                GeneratedCode = $"Código: {result.Code}\nVence a las {result.ExpiresAtUtc.Value.ToLocalTime():HH:mm:ss}.\nCrear otro código invalida el anterior.";
                GeneratedQrPayload = JsonSerializer.Serialize(new PairingQrData(
                    "miscelanea-ari-pairing", 1, address.Trim(), fingerprint.Trim(),
                    result.Code!, result.ExpiresAtUtc.Value));
            }
        }
        catch { Message = "No fue posible generar el código. Inténtalo de nuevo."; }
        finally { Password = string.Empty; IsBusy = false; }
    }

    private async Task LoadFingerprintAsync()
    {
        if (IsBusy || !CanLoadFingerprintFromFile || !CanEditConnection) return;
        IsBusy = true;
        Message = string.Empty;
        try
        {
            var fingerprintPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Miscelanea Ari", "Coordinator", "coordinator-certificate-fingerprint.txt");

            string fingerprint;
            if (File.Exists(fingerprintPath))
            {
                fingerprint = await File.ReadAllTextAsync(fingerprintPath);
            }
            else
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Selecciona el archivo de huella del coordinador"
                });
                if (file is null) return;

                await using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                fingerprint = await reader.ReadToEndAsync();
            }

            fingerprint = new string(fingerprint.Where(Uri.IsHexDigit).ToArray());
            if (fingerprint.Length != 64)
            {
                Message = "El archivo no contiene una huella SHA-256 válida de 64 caracteres hexadecimales.";
                return;
            }

            Fingerprint = fingerprint.ToUpperInvariant();
        }
        catch (OperationCanceledException)
        {
            // El usuario canceló el selector de archivos.
        }
        catch
        {
            Message = "No se pudo leer el archivo de huella. Comprueba que sea el archivo del coordinador.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool ApplyScannedQr(string rawValue)
    {
        if (IsWindowsHost) return false;
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
        if (IsWindowsHost || IsBusy || _confirmed || !_ready) return;
        IsBusy = true;
        try { Message = (await _client.PairAsync(Address, Fingerprint, Code)).Message; }
        catch { Message = "No fue posible vincular el dispositivo. Inténtalo de nuevo."; }
        finally { IsBusy = false; await LoadAsync(); }
    }

    private async Task DiscoverCoordinatorAsync()
    {
        if (!CanDiscover) return;
        IsBusy = true;
        DiscoveredCoordinators.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDiscoveredCoordinators)));
        _coordinatorSearchCancellation?.Dispose();
        _coordinatorSearchCancellation = new CancellationTokenSource();
        try
        {
            var candidates = await _discovery.FindAsync(TimeSpan.FromSeconds(6), _coordinatorSearchCancellation.Token);
            foreach (var candidate in candidates)
                DiscoveredCoordinators.Add(candidate);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDiscoveredCoordinators)));
            Message = candidates.Count == 0
                ? "No se encontró un coordinador en esta red. Comprueba que Windows y este dispositivo estén en la misma Wi-Fi. También puedes usar QR o configuración manual."
                : candidates.Count == 1
                    ? "Coordinador encontrado. Selecciónalo para completar la dirección de conexión."
                    : $"Se encontraron {candidates.Count} coordinadores. Elige el que pertenece a este negocio.";
        }
        catch (OperationCanceledException)
        {
            Message = "Se canceló la búsqueda. Puedes volver a intentarlo.";
        }
        catch (UnauthorizedAccessException)
        {
            Message = "Android no permitió buscar dispositivos en la red local. Revisa el permiso de red local y usa QR o conexión manual mientras tanto.";
        }
        catch
        {
            Message = "No se pudo buscar en esta red. El punto de acceso o el router puede bloquear el descubrimiento; puedes usar QR o configuración manual.";
        }
        finally
        {
            _coordinatorSearchCancellation?.Dispose();
            _coordinatorSearchCancellation = null;
            IsBusy = false;
            RefreshState();
        }
    }

    public void SelectCoordinator(CoordinatorCandidate? candidate)
    {
        if (candidate is null || !CanDiscover) return;
        SelectedCoordinator = candidate;
        Address = candidate.Address;
        Message = "Coordinador seleccionado. Puedes solicitar el vínculo seguro o continuar con QR o configuración manual.";
    }

    public async Task RequestDiscoveredPairingAsync()
    {
        if (!CanRequestDiscoveredPairing || SelectedCoordinator is null) return;
        DiscoveryPairingStartResult result;
        IsBusy = true;
        try
        {
            var deviceName = "Dispositivo Android";
            if (OperatingSystem.IsWindows()) deviceName = "Equipo Windows";
            try { deviceName = DeviceInfo.Current.Name; } catch { /* Use the platform fallback. */ }
            result = await _discoveryPairing.StartAsync(SelectedCoordinator, deviceName);
        }
        catch
        {
            Message = "No fue posible solicitar el vínculo. Vuelve a buscar el coordinador e inténtalo de nuevo.";
            return;
        }
        finally { IsBusy = false; }

        Message = result.Message;
        if (!result.Succeeded || result.RequestId is null) return;
        _discoveryRequestId = result.RequestId;
        DiscoveryVerificationCode = result.VerificationCode ?? string.Empty;
        DiscoveryRequestExpiry = result.ExpiresAtUtc;
        DiscoveryRequestStatus = "Compara este código con el que aparece en Windows.";
        RefreshState();

        _discoveryPollingCancellation?.Cancel();
        _discoveryPollingCancellation?.Dispose();
        _discoveryPollingCancellation = new CancellationTokenSource();
        await WaitForDiscoveredPairingDecisionAsync(result.RequestId.Value, _discoveryPollingCancellation.Token);
    }

    public void StopDiscoveryPolling() => _discoveryPollingCancellation?.Cancel();

    public void CancelCoordinatorSearch() => _coordinatorSearchCancellation?.Cancel();

    public void ResumeDiscoveryPolling()
    {
        if (_discoveryRequestId is not Guid requestId ||
            _discoveryPollingCancellation is { IsCancellationRequested: false }) return;
        _discoveryPollingCancellation?.Dispose();
        _discoveryPollingCancellation = new CancellationTokenSource();
        _ = WaitForDiscoveredPairingDecisionAsync(requestId, _discoveryPollingCancellation.Token);
    }

    private async Task WaitForDiscoveredPairingDecisionAsync(Guid requestId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _discoveryRequestId == requestId &&
               (DiscoveryRequestExpiry is null || DiscoveryRequestExpiry > DateTime.UtcNow))
        {
            DiscoveryPairingStatusResult status;
            IsBusy = true;
            try { status = await _discoveryPairing.GetStatusAsync(requestId, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { status = new(false, null, "No se pudo consultar el estado."); }
            finally { IsBusy = false; }

            if (!status.Succeeded)
            {
                DiscoveryRequestStatus = "Esperando respuesta del coordinador…";
                Message = status.Message ?? "La solicitud continúa pendiente.";
            }
            else if (status.Status == DiscoveryPairingStatus.WaitingForApproval)
            {
                DiscoveryRequestStatus = "Esperando que el Administrador compare el código y apruebe.";
            }
            else if (status.Status == DiscoveryPairingStatus.Approved)
            {
                DiscoveryRequestStatus = "Solicitud aprobada. Confirmando el vínculo…";
                IsBusy = true;
                PairingActionResult completed;
                try { completed = await _discoveryPairing.CompleteAsync(requestId, cancellationToken); }
                catch { completed = new(false, "No se pudo confirmar el vínculo. Inténtalo de nuevo antes de que venza la solicitud."); }
                finally { IsBusy = false; }
                Message = completed.Message;
                if (completed.Succeeded)
                {
                    DiscoveryRequestStatus = "Dispositivo vinculado. Inicia sesión con la cuenta central.";
                    _discoveryRequestId = null;
                    DiscoveryVerificationCode = string.Empty;
                    await LoadAsync();
                    RefreshState();
                    return;
                }
                DiscoveryRequestStatus = "La solicitud se aprobó, pero falta confirmar el vínculo. Reintentando…";
                RefreshState();
            }
            else if (status.Status == DiscoveryPairingStatus.Rejected)
            {
                await _discoveryPairing.CancelAsync(requestId, cancellationToken);
                DiscoveryRequestStatus = "Solicitud rechazada.";
                Message = "El Administrador rechazó la solicitud. Este dispositivo no quedó vinculado.";
                _discoveryRequestId = null;
                DiscoveryVerificationCode = string.Empty;
                await LoadAsync();
                RefreshState();
                return;
            }
            else if (status.Status == DiscoveryPairingStatus.Expired)
            {
                await _discoveryPairing.CancelAsync(requestId, cancellationToken);
                DiscoveryRequestStatus = "Solicitud vencida.";
                Message = "La solicitud venció. Vuelve a buscar el coordinador para iniciar otra.";
                _discoveryRequestId = null;
                DiscoveryVerificationCode = string.Empty;
                await LoadAsync();
                RefreshState();
                return;
            }
            else if (status.Status == DiscoveryPairingStatus.Completed)
            {
                var completed = await _discoveryPairing.CompleteAsync(requestId, cancellationToken);
                Message = completed.Message;
                if (completed.Succeeded)
                {
                    _discoveryRequestId = null;
                    DiscoveryVerificationCode = string.Empty;
                    await LoadAsync();
                    RefreshState();
                    return;
                }
                DiscoveryRequestStatus = "La solicitud se aprobó, pero falta confirmar el vínculo. Reintentando…";
                RefreshState();
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }

        if (_discoveryRequestId == requestId && DiscoveryRequestExpiry <= DateTime.UtcNow)
        {
            DiscoveryRequestStatus = "Solicitud vencida.";
            Message = "La solicitud venció. Vuelve a buscar el coordinador para iniciar otra.";
            _discoveryRequestId = null;
            DiscoveryVerificationCode = string.Empty;
            RefreshState();
        }
    }

    private async Task CancelDiscoveredPairingAsync()
    {
        if (_discoveryRequestId is not Guid requestId || IsBusy) return;
        IsBusy = true;
        PairingActionResult result;
        try { result = await _discoveryPairing.CancelAsync(requestId); }
        catch { result = new(false, "No se pudo contactar al coordinador para cancelar la solicitud."); }
        finally { IsBusy = false; }
        Message = result.Message;
        if (result.Succeeded)
        {
            _discoveryPollingCancellation?.Cancel();
            _discoveryRequestId = null;
            DiscoveryVerificationCode = string.Empty;
            DiscoveryRequestStatus = "Solicitud cancelada.";
            await LoadAsync();
        }
        RefreshState();
    }

    private async Task RefreshApprovalRequestsAsync()
    {
        if (!CanManageDiscoveredPairings || IsBusy) return;
        IsBusy = true;
        try
        {
            if (string.IsNullOrWhiteSpace(_adminApprovalToken) || _adminApprovalExpiry <= DateTime.UtcNow)
            {
                var session = await _discoveryApproval.CreateAdminSessionAsync(Address, Fingerprint, UserName, Password);
                Password = string.Empty;
                if (!session.Succeeded || string.IsNullOrWhiteSpace(session.Token))
                {
                    Message = session.Message;
                    _adminApprovalToken = null;
                    PendingDiscoveredPairings.Clear();
                    return;
                }
                _adminApprovalToken = session.Token;
                _adminApprovalExpiry = session.ExpiresAtUtc ?? DateTime.MinValue;
            }
            await LoadPendingApprovalRequestsAsync();
        }
        catch { Message = "No se pudieron consultar las solicitudes. Verifica la conexión y vuelve a intentarlo."; }
        finally { IsBusy = false; RefreshState(); }
    }

    private async Task LoadPendingApprovalRequestsAsync()
    {
        if (string.IsNullOrWhiteSpace(_adminApprovalToken)) return;
        var result = await _discoveryApproval.GetPendingAsync(Address, Fingerprint, _adminApprovalToken);
        if (!result.Succeeded)
        {
            Message = result.Message ?? "No se pudieron consultar las solicitudes pendientes.";
            if (_adminApprovalExpiry <= DateTime.UtcNow) _adminApprovalToken = null;
            return;
        }
        PendingDiscoveredPairings.Clear();
        foreach (var request in result.Pairings) PendingDiscoveredPairings.Add(request);
        Message = result.Pairings.Count == 0 ? "No hay solicitudes pendientes de aprobación." :
            $"Hay {result.Pairings.Count} solicitud(es). Comprueba que el código coincida con el dispositivo antes de aprobar.";
    }

    public async Task DecideDiscoveredPairingAsync(CoordinatorPendingPairing? request, bool approve)
    {
        if (request is null || !CanManageDiscoveredPairings || IsBusy) return;
        if (string.IsNullOrWhiteSpace(_adminApprovalToken) || _adminApprovalExpiry <= DateTime.UtcNow)
        {
            Message = "La sesión de aprobación venció. Ingresa de nuevo la contraseña del Administrador y actualiza las solicitudes.";
            _adminApprovalToken = null;
            return;
        }
        IsBusy = true;
        try
        {
            var result = approve
                ? await _discoveryApproval.ApproveAsync(Address, Fingerprint, _adminApprovalToken, request.RequestId)
                : await _discoveryApproval.RejectAsync(Address, Fingerprint, _adminApprovalToken, request.RequestId);
            Message = result.Message;
            if (result.Succeeded) await LoadPendingApprovalRequestsAsync();
            else if (_adminApprovalExpiry <= DateTime.UtcNow) _adminApprovalToken = null;
        }
        catch { Message = "No se pudo registrar la decisión. Actualiza la lista y vuelve a intentarlo."; }
        finally { IsBusy = false; RefreshState(); }
    }

    private IReadOnlyList<CoordinatorLanAddress> HostCandidates() =>
        CoordinatorHostAddressPolicy.Candidates(_hostEnvironment.GetLanAddresses());

    private static string CandidateSignature(IReadOnlyList<CoordinatorLanAddress> candidates) =>
        string.Join("|", candidates.Select(item => item.Address));

    private static string DescribeCandidates(IReadOnlyList<CoordinatorLanAddress> candidates) =>
        candidates.Count == 0
            ? "No se detectó una dirección LAN válida. Conecta Windows a la red del negocio y pulsa Confirmar dirección LAN."
            : (candidates.Count == 1 ? "Escribe la dirección LAN actual y pulsa Confirmar dirección LAN:\n"
                : "Se detectaron varias direcciones LAN. Escribe la que comparte red con Android y pulsa Confirmar dirección LAN:\n") +
                string.Join("\n", candidates.Select(item => $"{item.InterfaceName}: {item.Address}"));

    private void InvalidateHostAddress(string message)
    {
        _hostAddressValidated = false;
        GeneratedCode = GeneratedQrPayload = string.Empty;
        HostAddressMessage = message;
        RefreshState();
    }

    private void ValidateHostAddress(IReadOnlyList<CoordinatorLanAddress> candidates)
    {
        Address = new Uri(Address).AbsoluteUri;
        _validatedHostAddress = Address;
        _validatedHostCandidates = CandidateSignature(candidates);
        _hostAddressValidated = true;
        HostAddressMessage = $"Dirección LAN comprobada: {Address}";
        RefreshState();
    }

    private async Task<string> ReadHostFingerprintAsync()
    {
        var fingerprint = await _hostEnvironment.ReadInstalledFingerprintAsync();
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))
            throw new InvalidDataException("La huella del servicio no es SHA-256.");
        return fingerprint.ToUpperInvariant();
    }

    private void ConfirmHostAddress()
    {
        if (!IsWindowsHost || IsBusy || !_ready) return;
        IsBusy = true;
        try
        {
            var candidates = HostCandidates();
            if (string.IsNullOrWhiteSpace(Address) && candidates.Count == 1)
                Address = candidates[0].Address;
            if (!CoordinatorHostAddressPolicy.IsCandidate(Address, candidates))
            {
                InvalidateHostAddress("La dirección debe ser HTTPS, puerto 5080 y pertenecer a una interfaz LAN actual. " +
                    DescribeCandidates(candidates));
                return;
            }
            ValidateHostAddress(candidates);
        }
        catch
        {
            InvalidateHostAddress("No se pudieron consultar las interfaces LAN. Revisa la conexión y vuelve a confirmar.");
        }
        finally { IsBusy = false; }
    }

    private async Task<bool> CheckHostAddressAsync()
    {
        try
        {
            var candidates = HostCandidates();
            var fingerprint = await ReadHostFingerprintAsync();
            if (_hostAddressValidated && CoordinatorHostAddressPolicy.IsCandidate(Address, candidates) &&
                (candidates.Count == 1 || _validatedHostCandidates == CandidateSignature(candidates)) &&
                Fingerprint == fingerprint)
                return true;
            InvalidateHostAddress("Cambió la dirección LAN, sus interfaces o la huella del servicio. Revisa los datos y confirma la dirección antes de generar otro código. " +
                (candidates.Count == 1 ? string.Empty : DescribeCandidates(candidates)));
            Address = candidates.Count == 1 ? candidates[0].Address : string.Empty;
            Fingerprint = fingerprint;
            Message = HostAddressMessage;
        }
        catch
        {
            _ready = false;
            InvalidateHostAddress("No se pudo verificar la red LAN o el certificado instalado. No se mostrará ningún código o QR. Vuelve a abrir esta pantalla.");
            Message = HostAddressMessage;
        }
        return false;
    }

    public async Task<PairingActionResult> UnlinkAsync()
    {
        if (IsWindowsHost || IsBusy || !_confirmed || !_ready)
            return new(false, "No se pudo iniciar la desvinculación. Comprueba el estado del dispositivo y vuelve a intentar.");
        IsBusy = true;
        try
        {
            var result = await _client.UnlinkAsync();
            Message = result.Message;
            if (result.Succeeded) await LoadAsync();
            return result;
        }
        catch
        {
            var result = new PairingActionResult(false, "No fue posible desvincular este dispositivo. Sigue vinculado; sus datos locales se conservaron.");
            Message = result.Message;
            return result;
        }
        finally { IsBusy = false; RefreshState(); }
    }

    public async Task UpdateCoordinatorAddressAsync(Func<string, Task<bool>> confirm)
    {
        if (!CanUpdateCoordinatorAddress) return;
        IsBusy = true;
        try
        {
            AddressUpdateMessage = "Comprobando HTTPS con el certificado guardado…";
            var verification = await _client.VerifyCoordinatorAddressAsync(NewCoordinatorAddress);
            AddressUpdateMessage = verification.Message;
            if (!verification.Succeeded || verification.VerificationId is not { } id || verification.Address is null) return;
            if (!await confirm(verification.Address))
            {
                AddressUpdateMessage = "Actualización cancelada. La dirección anterior se conserva.";
                return;
            }
            AddressUpdateMessage = "Verificando y guardando la dirección…";
            var result = await _client.UpdateCoordinatorAddressAsync(id);
            AddressUpdateMessage = result.Message;
            if (result.Succeeded)
            {
                Address = verification.Address;
                NewCoordinatorAddress = string.Empty;
            }
        }
        catch
        {
            AddressUpdateMessage = "No se pudo completar la actualización. Revisa la dirección guardada antes de reintentar.";
        }
        finally { IsBusy = false; }
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowHostAddressConfirmation)));
        ((Command)ConfirmHostAddressCommand).ChangeCanExecute();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowAddressUpdate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUpdateCoordinatorAddress)));
        foreach (var name in new[] { nameof(ShowPairForm), nameof(CanUnlink), nameof(CanEditConnection), nameof(CanEditAddress), nameof(CanEditForm), nameof(CanScanQr), nameof(CanLoadFingerprintFromFile), nameof(CanShowGeneratedQr), nameof(LinkStatus), nameof(CanDiscover), nameof(CanRequestDiscoveredPairing), nameof(ShowDiscoveryRequest), nameof(CanCancelDiscoveredPairing), nameof(CanManageDiscoveredPairings) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ((Command)GenerateCodeCommand).ChangeCanExecute();
        ((Command)LoadFingerprintCommand).ChangeCanExecute();
        ((Command)PairCommand).ChangeCanExecute();
        ((Command)DiscoverCommand).ChangeCanExecute();
        ((Command)RefreshApprovalRequestsCommand).ChangeCanExecute();
        ((Command)CancelDiscoveredPairingCommand).ChangeCanExecute();
    }

    private sealed record PairingQrData(string Type, int Version, string Address, string Fingerprint,
        string Code, DateTime ExpiresAtUtc);
}
