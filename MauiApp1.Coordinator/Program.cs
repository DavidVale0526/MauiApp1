using Microsoft.AspNetCore.Http.HttpResults;
using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Coordinator.Persistence;

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
    return;
}

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

builder.WebHost.UseUrls(builder.Configuration["Coordinator:Url"] ?? "http://0.0.0.0:5080");
builder.Services.AddSignalR();

var configuredDataDirectory = builder.Configuration["Coordinator:DataDirectory"];
var dataDirectory = string.IsNullOrWhiteSpace(configuredDataDirectory)
    ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Miscelanea Ari",
        "Coordinator")
    : configuredDataDirectory;
Directory.CreateDirectory(dataDirectory);
var centralDatabasePath = Path.Combine(dataDirectory, "miscelanea-ari-central.db");
builder.Services.AddLocalPersistence(centralDatabasePath);

var app = builder.Build();

if (!File.Exists(centralDatabasePath))
{
    throw new InvalidOperationException(
        "La base central no existe. Inicialízala con --bootstrap-from <ruta-base-Windows> antes de iniciar el coordinador.");
}

await app.Services.GetRequiredService<ILocalDatabaseInitializer>().InitializeAsync();

// This liveness endpoint exposes no account, inventory, or transaction data.
app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")));

app.Run();

static string? ReadOption(string[] arguments, string name)
{
    var optionIndex = Array.FindIndex(arguments, argument =>
        string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
    return optionIndex >= 0 && optionIndex + 1 < arguments.Length
        ? arguments[optionIndex + 1]
        : null;
}

internal sealed record HealthResponse(string Status);
