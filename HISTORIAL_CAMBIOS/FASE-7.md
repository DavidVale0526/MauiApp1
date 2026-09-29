# Fase 7 — Coordinador LAN y sincronización

## T7.1 — Crear la base del coordinador Windows (en curso)

### Decisiones aprobadas

- Ejecutar el coordinador como Servicio de Windows con API ASP.NET Core y SignalR, disponible solo por la LAN y con puerto configurable.
- El coordinador será la autoridad de cuentas compartidas; dispositivos autorizados iniciarán sesión por LAN y conservarán acceso local cuando estén desconectados.
- Un Administrador debe vincular los dispositivos antes de que accedan a datos compartidos.
- La base local de Windows será el origen central inicial. Se conservarán respaldos de Windows y Android; Android no se reemplazará ni combinará automáticamente.
- Valor inicial de desarrollo del puerto configurable: 5080. La dirección y las reglas del firewall quedan sujetas a instalación/configuración en T7.1.

### Creados

- `MauiApp1.Coordinator/MauiApp1.Coordinator.csproj` — host web .NET 10 independiente, referencia Application e Infrastructure e incorpora soporte para Windows Services.
- `MauiApp1.Coordinator/Program.cs` — host de API, ciclo de vida Windows Service, registro de SignalR, migración/inicialización central EF Core y ruta `GET /health` sin datos de negocio.
- `MauiApp1.Coordinator/Persistence/CoordinatorDatabaseBootstrapper.cs` — valida integridad SQLite y crea un respaldo fechado más una copia central a partir de la base Windows, sin tocar el origen ni sobrescribir una base central existente.
- `MauiApp1.Coordinator/appsettings.json` — URL y directorio de datos configurables y niveles de registro.
- `MauiApp1.Coordinator/Deployment/install-coordinator.ps1` y `remove-coordinator.ps1` — preparación/eliminación del servicio, certificado HTTPS y regla de firewall privada; todavía no ejecutados sobre el equipo.
- `MauiApp1.Coordinator/Properties/launchSettings.json` — perfil local de desarrollo con HTTP limitado a loopback y base efímera de desarrollo separada.

### Modificados

- `MauiApp1.slnx` — agrega el proyecto coordinador.
- `D:\Trabajo\Tesis\TASK.md` — marca creado el host y la base central; registra lo que falta en T7.1.
- `MauiApp1/ARCHITECTURE.md` — registra ubicación, configuración, importación y límites actuales del host/base.
- `MauiApp1.Coordinator/Program.cs` — captura fallos de configuración y evita excepciones sin controlar; permite crear una base vacía solo en Development cuando el indicador explícito está habilitado.
- `D:\Trabajo\Tesis\TASK.md` — marca el perfil de desarrollo y el manejo de errores como listos.
- `HISTORIAL_CAMBIOS/README.md` — índice de Fase 7.

### Verificación

- `dotnet build .\MauiApp1.Coordinator\MauiApp1.Coordinator.csproj --no-restore`: correcto, 0 advertencias y 0 errores. La restauración normal quedó impedida en este entorno por falta de acceso a `C:\Users\HP Pavilioh Gaming\AppData\Roaming\NuGet\NuGet.Config`; se usaron los paquetes ya restaurados.
- Arranque local con directorio temporal: EF Core aplicó las migraciones, creó `miscelanea-ari-central.db` y `GET /health` en `127.0.0.1:5081` devolvió `{"status":"ok"}`.
- Smoke check de `--bootstrap-from` con base temporal: creó la copia central y el respaldo fechado; la original siguió disponible y el host arrancó.
- Perfil de desarrollo: inicializó la base separada, escuchó solo en `127.0.0.1:5080` y `GET /health` respondió `{"status":"ok"}`.
- Perfil de producción sin huella de certificado: informó el requisito faltante y terminó con código 1, sin una excepción JIT.
- No se instaló ni inició como servicio persistente, no se cambió el firewall y no se expusieron datos de negocio. La importación real desde Windows y el respaldo de Android quedan pendientes antes de sincronizar; también faltan API de operaciones, autenticación, vinculación, hub SignalR funcional y pruebas LAN.
