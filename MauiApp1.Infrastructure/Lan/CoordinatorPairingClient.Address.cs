using System.Net.Http.Json;
using MauiApp1.Application.Lan;

namespace MauiApp1.Infrastructure.Lan;

public sealed partial class CoordinatorPairingClient
{
    // Kept only in memory: a caller cannot save an address that was not verified.
    private AddressVerification? _addressVerification;

    public async Task<CoordinatorAddressVerification> VerifyCoordinatorAddressAsync(string address)
    {
        await _pairingLock.WaitAsync();
        try
        {
            _addressVerification = null;
            var link = await ReadLinkAsync();
            if (link?.IsConfirmed != true)
                return new(false, "Se necesita una vinculación confirmada para actualizar la dirección.");
            if (!TryValidateConnection(address, link.CertificateFingerprint, out var uri, out var fingerprint, out var error))
                return new(false, error);
            if (!await CheckAddressHealthAsync(uri!, fingerprint))
                return new(false, "El Coordinador no respondió correctamente. La dirección anterior se conserva.");
            var verification = new AddressVerification(Guid.NewGuid(), uri!.AbsoluteUri, link,
                DateTime.UtcNow.AddMinutes(2));
            _addressVerification = verification;
            return new(true, "Dirección comprobada con el certificado guardado. Confirma para guardarla.",
                verification.Id, verification.Address);
        }
        catch
        {
            return new(false, "No se pudo verificar la dirección con el certificado guardado. No se modificó la vinculación.");
        }
        finally { _pairingLock.Release(); }
    }

    public async Task<PairingActionResult> UpdateCoordinatorAddressAsync(Guid verificationId)
    {
        await _pairingLock.WaitAsync();
        try
        {
            var verification = _addressVerification;
            _addressVerification = null; // Single use, including unsuccessful attempts.
            if (verification is null || verification.Id != verificationId || verification.ExpiresAtUtc <= DateTime.UtcNow)
                return new(false, "Vuelve a comprobar la dirección antes de guardarla.");
            var current = await ReadLinkAsync();
            if (current?.IsConfirmed != true || !SameLink(current, verification.Link))
                return new(false, "La vinculación cambió. Vuelve a comprobar la dirección; no se guardaron cambios.");
            // Recheck after the confirmation dialog without sending business operations.
            if (!TryValidateConnection(verification.Address, current.CertificateFingerprint,
                    out var uri, out var fingerprint, out _) || !await CheckAddressHealthAsync(uri!, fingerprint))
                return new(false, "No se pudo volver a verificar el Coordinador. La dirección anterior se conserva.");
            await storage.SaveAsync(new CoordinatorDeviceLink
            {
                Address = verification.Address,
                CertificateFingerprint = current.CertificateFingerprint,
                DeviceId = current.DeviceId,
                DeviceToken = current.DeviceToken,
                IsConfirmed = current.IsConfirmed,
                PendingCode = current.PendingCode
            });
            return new(true, "Dirección del Coordinador actualizada. La sincronización automática existente puede comenzar al recuperar conectividad.");
        }
        catch
        {
            return new(false, "No se pudo completar la actualización de dirección. Revisa el estado de la vinculación antes de reintentar.");
        }
        finally { _pairingLock.Release(); }
    }

    private static async Task<bool> CheckAddressHealthAsync(Uri address, string fingerprint)
    {
        using var client = CreateClient(address, fingerprint);
        return (await client.GetFromJsonAsync<HealthResponse>("health"))?.Status == "ok";
    }

    private static bool SameLink(CoordinatorDeviceLink left, CoordinatorDeviceLink right) =>
        left.Address == right.Address && left.CertificateFingerprint == right.CertificateFingerprint &&
        left.DeviceId == right.DeviceId && left.DeviceToken == right.DeviceToken &&
        left.IsConfirmed == right.IsConfirmed && left.PendingCode == right.PendingCode;

    // A class deliberately avoids a generated ToString exposing the saved link.
    private sealed class AddressVerification(Guid id, string address, CoordinatorDeviceLink link, DateTime expiresAtUtc)
    {
        public Guid Id { get; } = id;
        public string Address { get; } = address;
        public CoordinatorDeviceLink Link { get; } = link;
        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
    }
}
