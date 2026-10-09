# Comprobación aislada de sincronización entre tres instalaciones

Ejecutar desde la raíz de MauiApp1:

```powershell
dotnet run --project Verification/SyncThreeDevices/SyncThreeDevices.csproj
```

La comprobación usa tres bases SQLite temporales, un coordinador HTTPS en localhost,
los clientes de sincronización de producción y el hub/broadcaster SignalR real.
No modifica la base instalada, el servicio ni las cuentas del negocio. El proceso
necesita permiso para conexiones locales. La clave del certificado temporal se
recarga para que Windows Schannel pueda usarla; no se instala un certificado.

Comprueba:

- Descarga de 105 operaciones en una sola sincronización, con dos cuentas de cajero distintas.
- Identificación del autor cuando SQLite contiene GUID con distinta capitalización.
- Exclusión de duplicados históricos idénticos sin borrar el historial.
- Repetición idempotente de descargas.
- Publicación automática de Windows y recepción por SignalR en ambos clientes,
  con los trabajadores de consulta periódica de los receptores detenidos.
- Creación y edición de categorías y productos.
- Envío automático desde ambos cajeros y notificación para refrescar las vistas.
- Conservación de cambios al detener el trabajador y recuperación al iniciarlo.

Resultado del 8 de octubre de 2026: todas las comprobaciones pasaron. Una creación
desde Windows llegó a las dos bases receptoras en 285 ms en localhost.
Esto no mide la pantalla Android ni valida la latencia de una red Wi-Fi real.
La instalación en los tres dispositivos y su comprobación visual siguen pendientes.

## Aplicar la corrección al negocio

1. Cerrar la app de Windows y actualizar el servicio con PowerShell 7 como administrador:

   ```powershell
   & "D:\Trabajo\Tesis\MauiApp1\MauiApp1.Coordinator\Deployment\update-coordinator.ps1" -WindowsAppUser "$env:USERDOMAIN\$env:USERNAME"
   ```

2. Compilar y ejecutar la app de Windows actualizada.
3. Instalar el APK actualizado en los dos teléfonos sobre la instalación existente,
   sin desinstalar ni borrar datos. APK autónomo de esta corrección:
   `MauiApp1/bin/SyncFix-2026-10-08/MiscelaneaAri-Android.apk`.
4. Abrir las tres apps en la misma LAN y comprobar el estado de conexión en la vista
   de sincronización. Si la sesión central venció, iniciar sesión con el coordinador disponible.
5. Mantener Categorías abierta en ambos teléfonos. Crear y renombrar una categoría
   desde Windows y desde cada teléfono. Repetir con un producto; comprobar que las
   otras pantallas cambian sin salir de la vista ni pulsar sincronización manual.
6. Registrar el tiempo en dispositivos físicos y cualquier conflicto o mensaje de error.
