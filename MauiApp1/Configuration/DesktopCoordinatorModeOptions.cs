using System.Text.Json;
#if WINDOWS
using Microsoft.Win32;
using System.Text.RegularExpressions;
#endif

namespace MauiApp1.Configuration;

public enum DesktopCoordinatorMode
{
    Host,
    Client
}

/// <summary>Explicit role and central data location for the Windows MAUI desktop app.</summary>
public sealed class DesktopCoordinatorModeOptions
{
    private const string ProductDirectoryName = "Miscelanea Ari";
    private const string DesktopSettingsDirectoryName = "Desktop";
    private const string SettingsFileName = "windows-app-mode.json";
    private const string CentralDatabaseFileName = "miscelanea-ari-central.db";

    private DesktopCoordinatorModeOptions(
        DesktopCoordinatorMode mode,
        string? centralDataDirectory,
        string? settingsPath)
    {
        Mode = mode;
        CentralDataDirectory = centralDataDirectory;
        SettingsPath = settingsPath;
    }

    public DesktopCoordinatorMode Mode { get; }
    public string? CentralDataDirectory { get; }
    public string? SettingsPath { get; }
    public bool IsHost => Mode == DesktopCoordinatorMode.Host;

    public static DesktopCoordinatorModeOptions LocalClient { get; } =
        new(DesktopCoordinatorMode.Client, null, null);

    public static string GetWindowsSettingsPath()
    {
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(commonData, ProductDirectoryName, DesktopSettingsDirectoryName, SettingsFileName);
    }

    public static DesktopCoordinatorModeOptions LoadForWindows()
    {
        var settingsPath = GetWindowsSettingsPath();
        if (!File.Exists(settingsPath))
        {
            // A regular Windows installation is a client by default. Only the
            // coordinator installer should explicitly opt this machine into Host.
            // This keeps client setup out of PowerShell and prevents a leftover
            // central SQLite file from deciding which database the app opens.
            return LocalClient;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Coordinator", out var coordinator) ||
                coordinator.ValueKind != JsonValueKind.Object ||
                !coordinator.TryGetProperty("Mode", out var modeElement) ||
                modeElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Falta la propiedad Coordinator.Mode.");
            }

            var mode = modeElement.GetString();
            if (string.Equals(mode, "Client", StringComparison.OrdinalIgnoreCase))
                return new DesktopCoordinatorModeOptions(DesktopCoordinatorMode.Client, null, settingsPath);

            if (!string.Equals(mode, "Host", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Coordinator.Mode solo admite Host o Client.");

            if (!coordinator.TryGetProperty("DataDirectory", out var directoryElement) ||
                directoryElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(directoryElement.GetString()))
            {
                throw new InvalidDataException("El modo Host requiere Coordinator.DataDirectory.");
            }

            string dataDirectory;
            try
            {
                dataDirectory = Path.GetFullPath(directoryElement.GetString()!);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidDataException("Coordinator.DataDirectory no contiene una ruta válida.", exception);
            }
            var centralDatabasePath = Path.Combine(dataDirectory, CentralDatabaseFileName);
            if (!File.Exists(centralDatabasePath))
            {
                throw new InvalidDataException(
                    $"El modo Host apunta a una base central inexistente: {centralDatabasePath}. No se usará la base local como alternativa.");
            }

#if WINDOWS
            var serviceDataDirectory = ReadCoordinatorServiceDataDirectory();
            if (!string.Equals(dataDirectory, serviceDataDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Coordinator.DataDirectory no coincide con la carpeta configurada en el servicio Windows MiscelaneaAriCoordinator ({serviceDataDirectory}). No se abrirá ninguna de las dos bases.");
            }
#endif

            return new DesktopCoordinatorModeOptions(DesktopCoordinatorMode.Host, dataDirectory, settingsPath);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"La configuración de modo de Windows no contiene JSON válido. Corrige {settingsPath} o vuelve a ejecuta el configurador.",
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException(
                $"La configuración de modo de Windows no es válida ({settingsPath}): {exception.Message}",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"No se pudo leer la configuración o la base central de Windows. Revisa los permisos y el archivo {settingsPath}.",
                exception);
        }
        catch (System.Security.SecurityException exception)
        {
            throw new InvalidOperationException(
                $"Windows no permitió leer la configuración o la ruta central. Revisa los permisos de {settingsPath}.",
                exception);
        }
    }

    public string GetCentralDatabasePath()
    {
        if (!IsHost || string.IsNullOrWhiteSpace(CentralDataDirectory))
            throw new InvalidOperationException("La ruta central solo está disponible en modo Host.");

        return Path.Combine(CentralDataDirectory, CentralDatabaseFileName);
    }

#if WINDOWS
    private static string ReadCoordinatorServiceDataDirectory()
    {
        using var serviceKey = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\MiscelaneaAriCoordinator");
        var imagePath = serviceKey?.GetValue("ImagePath") as string;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new InvalidDataException(
                "No se encontró el servicio Windows MiscelaneaAriCoordinator o no tiene ImagePath configurado. Instala el coordinador antes de abrir la aplicación en modo Host.");
        }

        var match = Regex.Match(
            imagePath,
            @"--Coordinator:DataDirectory=(?:""(?<quoted>[^""]+)""|(?<plain>\S+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var configuredDirectory = match.Success
            ? match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value
            : null;
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            throw new InvalidDataException(
                "El servicio Windows no declara --Coordinator:DataDirectory. Reinstala o actualiza el servicio para que la aplicación pueda validar la misma base central.");
        }

        return Path.GetFullPath(configuredDirectory);
    }
#endif
}
