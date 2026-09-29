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

### Modificados

- `MauiApp1.slnx` — agrega el proyecto coordinador.
- `D:\Trabajo\Tesis\TASK.md` — marca creado el host y la base central; registra lo que falta en T7.1.
- `MauiApp1/ARCHITECTURE.md` — registra ubicación, configuración, importación y límites actuales del host/base.
- `HISTORIAL_CAMBIOS/README.md` — índice de Fase 7.

### Verificación

- `dotnet build .\MauiApp1.Coordinator\MauiApp1.Coordinator.csproj`: correcto, 0 advertencias y 0 errores.
- Arranque local con directorio temporal: EF Core aplicó las migraciones, creó `miscelanea-ari-central.db` y `GET /health` en `127.0.0.1:5081` devolvió `{"status":"ok"}`.
- Smoke check de `--bootstrap-from` con base temporal: creó la copia central y el respaldo fechado; la original siguió disponible y el host arrancó.
- No se instaló ni inició como servicio persistente, no se cambió el firewall y no se expusieron datos de negocio. La importación real desde Windows y el respaldo de Android quedan pendientes antes de sincronizar; también faltan API de operaciones, autenticación, vinculación, hub SignalR funcional y pruebas LAN.
