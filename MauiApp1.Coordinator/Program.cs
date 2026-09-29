using System.Net;
using System.Security.Cryptography.X509Certificates;
using MauiApp1.Application.Storage;
using MauiApp1.Coordinator.Persistence;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var bootstrapSource = ReadOption(args, "--bootstrap-from");
            if (bootstrapSource is not null)
            {
                var dataDirectoryOption = ReadOption(args, "--data-directory");
                var bootstrapDataDirectory = string.IsNullOrWhiteSpace(dataDirectoryOption)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Miscelanea Ari", "Coordinator")
                    : dataDirectoryOption;
                var createdPath = await CoordinatorDatabaseBootstrapper.BootstrapAsync(bootstrapSource, bootstrapDataDirectory);
                Console.WriteLine($"Base central inicial creada desde Windows: {createdPath}");
                return 0;
            }

            return await RunCoordinatorAsync(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"No se pudo iniciar el coordinador: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunCoordinatorAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseWindowsService(options => options.ServiceName = "Miscelanea Ari Coordinator");
        builder.Logging.ClearProviders();
        if (OperatingSystem.IsWindows() && !Environment.UserInteractive)
        {
            builder.Logging.AddEventLog();
        }
        else
        {
            builder.Logging.AddConsole();
        }

        var isDevelopment = builder.Environment.IsDevelopment();
        var allowEmptyDevelopmentDatabase = isDevelopment &&
            builder.Configuration.GetValue<bool>("Coordinator:AllowEmptyDevelopmentDatabase");
        var endpointUrl = builder.Configuration["Coordinator:Url"] ?? "https://0.0.0.0:5080";
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("Coordinator:Url debe ser una URL absoluta.");
        }

        if (endpoint.Scheme == Uri.UriSchemeHttps)
        {
            var certificate = LoadCoordinatorCertificate(builder.Configuration["Coordinator:CertificateThumbprint"]);
            builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(endpoint.Port,
                listenOptions => listenOptions.UseHttps(certificate)));
        }
        else if (isDevelopment && endpoint.Scheme == Uri.UriSchemeHttp &&
                 IPAddress.TryParse(endpoint.Host, out var listenAddress) && IPAddress.IsLoopback(listenAddress))
        {
            builder.WebHost.ConfigureKestrel(options => options.Listen(listenAddress, endpoint.Port));
        }
        else
        {
            throw new InvalidOperationException(
                "El coordinador requiere HTTPS. HTTP solo se permite en Development y ligado a loopback.");
        }

        builder.Services.AddSignalR();

        var configuredDataDirectory = builder.Configuration["Coordinator:DataDirectory"];
        var dataDirectory = !string.IsNullOrWhiteSpace(configuredDataDirectory)
            ? configuredDataDirectory
            : allowEmptyDevelopmentDatabase
                ? Path.Combine(AppContext.BaseDirectory, "coordinator-development")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Miscelanea Ari", "Coordinator");
        Directory.CreateDirectory(dataDirectory);
        var centralDatabasePath = Path.Combine(dataDirectory, "miscelanea-ari-central.db");
        builder.Services.AddLocalPersistence(centralDatabasePath);

        var app = builder.Build();

        if (!File.Exists(centralDatabasePath) && !allowEmptyDevelopmentDatabase)
        {
            throw new InvalidOperationException(
                "La base central no existe. Inicialízala con --bootstrap-from <ruta-base-Windows> antes de iniciar el coordinador.");
        }

        await app.Services.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();

        // This liveness endpoint exposes no account, inventory, or transaction data.
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")));

        await app.RunAsync();
        return 0;
    }

    private static string? ReadOption(string[] arguments, string name)
    {
        var optionIndex = Array.FindIndex(arguments, argument =>
            string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return optionIndex >= 0 && optionIndex + 1 < arguments.Length
            ? arguments[optionIndex + 1]
            : null;
    }

    private static X509Certificate2 LoadCoordinatorCertificate(string? thumbprint)
    {
        var normalizedThumbprint = thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalizedThumbprint))
        {
            throw new InvalidOperationException(
                "Falta Coordinator:CertificateThumbprint. Instala el certificado local antes de iniciar el servicio.");
        }

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var certificate = store.Certificates
            .Find(X509FindType.FindByThumbprint, normalizedThumbprint, validOnly: false)
            .OfType<X509Certificate2>()
            .FirstOrDefault(item => item.HasPrivateKey);

        return certificate ?? throw new InvalidOperationException(
            "No se encontró el certificado coordinador con clave privada en el almacén LocalMachine/My.");
    }
}

internal sealed record HealthResponse(string Status);
