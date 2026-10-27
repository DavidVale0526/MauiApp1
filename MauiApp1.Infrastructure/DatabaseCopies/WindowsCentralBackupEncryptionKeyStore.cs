using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text.Json;
using MauiApp1.Application.Backups;

namespace MauiApp1.Infrastructure.Backups;

/// <summary>Persists the data key only as a machine-protected DPAPI blob in the coordinator data directory.</summary>
public sealed class WindowsCentralBackupEncryptionKeyStore : ICentralBackupEncryptionKeyStore
{
    private const uint CryptProtectLocalMachine = 0x4;
    private const string FileName = "central-backup-encryption-key.dpapi";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _keyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WindowsCentralBackupEncryptionKeyStore(string coordinatorDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinatorDataDirectory);
        _keyPath = Path.Combine(Path.GetFullPath(coordinatorDataDirectory), FileName);
    }

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(_keyPath));
    }

    public async Task<BackupEncryptionKeyMaterial?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_keyPath)) return null;
            var protectedBytes = await File.ReadAllBytesAsync(_keyPath, cancellationToken).ConfigureAwait(false);
            var clearBytes = WindowsDpapi.Unprotect(protectedBytes);
            try
            {
                var stored = JsonSerializer.Deserialize<StoredKeyMaterial>(clearBytes, JsonOptions)
                    ?? throw new InvalidDataException("El archivo de protección de respaldos está vacío o dañado.");
                var dataKey = Convert.FromBase64String(stored.DataKeyBase64);
                if (dataKey.Length != BackupEncryptionCryptography.DataKeyLengthBytes)
                {
                    CryptographicOperations.ZeroMemory(dataKey);
                    throw new InvalidDataException("La clave protegida de respaldos tiene una longitud no válida.");
                }

                return new BackupEncryptionKeyMaterial(dataKey, stored.RecoveryEnvelope);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("El archivo de protección de respaldos está dañado.", exception);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("La clave protegida de respaldos está dañada.", exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ConfigureAsync(string recoveryPassphrase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryPassphrase);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_keyPath))
                throw new InvalidOperationException("El cifrado de respaldos ya está configurado. No se reemplazó la clave existente.");

            var dataKey = RandomNumberGenerator.GetBytes(BackupEncryptionCryptography.DataKeyLengthBytes);
            try
            {
                var envelope = BackupEncryptionCryptography.CreateEnvelope(dataKey, recoveryPassphrase);
                await SaveProtectedMaterialAsync(dataKey, envelope, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnlockFromBackupAsync(
        BackupEncryptionEnvelope recoveryEnvelope,
        string recoveryPassphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryEnvelope);
        ArgumentNullException.ThrowIfNull(recoveryPassphrase);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recoveredDataKey = BackupEncryptionCryptography.UnwrapDataKey(recoveryEnvelope, recoveryPassphrase);
            try
            {
                if (File.Exists(_keyPath))
                {
                    var existing = await ReadWithoutGateAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (existing is not null && !CryptographicOperations.FixedTimeEquals(existing.DataKey, recoveredDataKey))
                            throw new InvalidOperationException("La copia usa otra clave de respaldo distinta a la que ya está configurada en esta PC.");
                    }
                    finally
                    {
                        if (existing is not null) CryptographicOperations.ZeroMemory(existing.DataKey);
                    }
                }

                await SaveProtectedMaterialAsync(recoveredDataKey, recoveryEnvelope, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(recoveredDataKey);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BackupEncryptionKeyMaterial?> ReadWithoutGateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_keyPath)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(_keyPath, cancellationToken).ConfigureAwait(false);
        var clearBytes = WindowsDpapi.Unprotect(protectedBytes);
        try
        {
            var stored = JsonSerializer.Deserialize<StoredKeyMaterial>(clearBytes, JsonOptions)
                ?? throw new InvalidDataException("El archivo de protección de respaldos está vacío o dañado.");
            var dataKey = Convert.FromBase64String(stored.DataKeyBase64);
            if (dataKey.Length != BackupEncryptionCryptography.DataKeyLengthBytes)
            {
                CryptographicOperations.ZeroMemory(dataKey);
                throw new InvalidDataException("La clave protegida de respaldos tiene una longitud no válida.");
            }

            return new BackupEncryptionKeyMaterial(dataKey, stored.RecoveryEnvelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private async Task SaveProtectedMaterialAsync(
        byte[] dataKey,
        BackupEncryptionEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_keyPath)!;
        Directory.CreateDirectory(directory);
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(
            new StoredKeyMaterial(Convert.ToBase64String(dataKey), envelope), JsonOptions);
        var protectedBytes = WindowsDpapi.Protect(clearBytes);
        var temporaryPath = _keyPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
            RestrictFilePermissions(temporaryPath);
            File.Move(temporaryPath, _keyPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void RestrictFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La clave local de respaldos requiere Windows DPAPI.");

        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("No se pudo identificar la cuenta de Windows actual.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(identity,
            FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Delete,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Delete,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(new FileInfo(path), security);
    }

    private sealed record StoredKeyMaterial(string DataKeyBase64, BackupEncryptionEnvelope RecoveryEnvelope);

    private static class WindowsDpapi
    {
        public static byte[] Protect(byte[] input) => Transform(input, protect: true);
        public static byte[] Unprotect(byte[] input) => Transform(input, protect: false);

        private static byte[] Transform(byte[] input, bool protect)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("La clave local de respaldos requiere Windows DPAPI.");

            var inputPointer = Marshal.AllocHGlobal(input.Length);
            try
            {
                Marshal.Copy(input, 0, inputPointer, input.Length);
                var inputBlob = new DataBlob { Size = input.Length, Data = inputPointer };
                DataBlob outputBlob;
                var success = protect
                    ? CryptProtectData(ref inputBlob, "Miscelánea Ari backup key", IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, CryptProtectLocalMachine, out outputBlob)
                    : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, 0, out outputBlob);
                if (!success)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows no pudo proteger o abrir la clave de respaldos.");

                try
                {
                    var output = new byte[outputBlob.Size];
                    Marshal.Copy(outputBlob.Data, output, 0, outputBlob.Size);
                    return output;
                }
                finally
                {
                    if (!protect && outputBlob.Data != IntPtr.Zero)
                        ZeroNativeMemory(outputBlob.Data, outputBlob.Size);
                    if (outputBlob.Data != IntPtr.Zero) LocalFree(outputBlob.Data);
                }
            }
            finally
            {
                ZeroNativeMemory(inputPointer, input.Length);
                Marshal.FreeHGlobal(inputPointer);
            }
        }

        private static void ZeroNativeMemory(IntPtr pointer, int length)
        {
            if (pointer == IntPtr.Zero || length <= 0) return;
            var zeros = new byte[length];
            Marshal.Copy(zeros, 0, pointer, length);
            CryptographicOperations.ZeroMemory(zeros);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr optionalEntropy,
            IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr optionalEntropy,
            IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
