using MauiApp1.Application.Lan;
using MauiApp1.Configuration;
using MauiApp1.Infrastructure.Lan;

namespace MauiApp1.Services;

public sealed class CoordinatorDeviceDirectory(
    ICoordinatorDeviceClient coordinatorClient,
    DesktopCoordinatorModeOptions desktopMode) : ICoordinatorDeviceDirectory
{
    public async Task<CoordinatorDeviceListResult> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows() && desktopMode.IsHost)
        {
            try
            {
                var store = new CoordinatorDevicePresenceStore(desktopMode.GetCentralDatabasePath());
                return CoordinatorDeviceListResult.Success(await store.GetDevicesAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return CoordinatorDeviceListResult.Failure(
                    "No se pudo consultar el registro de dispositivos del coordinador. Comprueba que el servicio esté actualizado y activo.");
            }
        }

        return await coordinatorClient.GetDevicesAsync(cancellationToken);
    }
}
