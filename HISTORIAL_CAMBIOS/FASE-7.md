# Fase 7 — Coordinador LAN y sincronización

## T7.1 — Crear la base del coordinador Windows (en curso)

### Vinculación segura inicial de dispositivos (backend, en curso)

- `MauiApp1.Coordinator/Pairing/CoordinatorPairingStore.cs` crea un código de un solo uso con vencimiento de cinco minutos y registra el `DeviceId` al canjearlo. El código lleva sal antes de almacenarse; el dispositivo recibe un token aleatorio de 256 bits cuyo hash queda en la base central.
- `POST /api/devices/pairing-code` exige HTTPS y credenciales válidas de Administrador de la base central; `POST /api/devices/pair` exige HTTPS, tiene límite de diez intentos por minuto por IP y rechaza códigos vencidos/reutilizados. Generar otro código invalida el anterior.
- Las pantallas, fijación de huella y almacenamiento seguro se implementaron en el siguiente subpaso, registrado abajo. Quedan pendientes la validación/revocación del token para operaciones y la prueba real en dispositivos.

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
- `MauiApp1.Coordinator/Pairing/CoordinatorPairingStore.cs` — genera códigos de vinculación con vencimiento, canje de un solo uso y registro de tokens de dispositivo en la base central.
- `MauiApp1.Coordinator/appsettings.json` — URL y directorio de datos configurables y niveles de registro.
- `MauiApp1.Coordinator/Deployment/install-coordinator.ps1` y `remove-coordinator.ps1` — preparación/eliminación del servicio, certificado HTTPS y regla de firewall privada; todavía no ejecutados sobre el equipo.
- `MauiApp1.Coordinator/Properties/launchSettings.json` — perfil local de desarrollo con HTTP limitado a loopback y base efímera de desarrollo separada.

### Modificados

- `MauiApp1.slnx` — agrega el proyecto coordinador.
- `D:\Trabajo\Tesis\TASK.md` — marca creado el host y la base central; registra lo que falta en T7.1.
- `MauiApp1/ARCHITECTURE.md` — registra ubicación, configuración, importación y límites actuales del host/base.
- `MauiApp1.Coordinator/Program.cs` — captura fallos de configuración y evita excepciones sin controlar; permite crear una base vacía solo en Development cuando el indicador explícito está habilitado.
- `MauiApp1.Coordinator/Program.cs` — agrega las rutas HTTPS para pedir código (requiere reautenticación del Administrador) y canjearlo; limita intentos por IP.
- `D:\Trabajo\Tesis\TASK.md` — marca el perfil de desarrollo y el manejo de errores como listos.
- `HISTORIAL_CAMBIOS/README.md` — índice de Fase 7.

### Verificación

- `dotnet build .\MauiApp1.Coordinator\MauiApp1.Coordinator.csproj --no-restore`: correcto, 0 advertencias y 0 errores. La restauración normal quedó impedida en este entorno por falta de acceso a `C:\Users\HP Pavilioh Gaming\AppData\Roaming\NuGet\NuGet.Config`; se usaron los paquetes ya restaurados.
- Arranque local con directorio temporal: EF Core aplicó las migraciones, creó `miscelanea-ari-central.db` y `GET /health` en `127.0.0.1:5081` devolvió `{"status":"ok"}`.
- Smoke check de `--bootstrap-from` con base temporal: creó la copia central y el respaldo fechado; la original siguió disponible y el host arrancó.
- Perfil de desarrollo: inicializó la base separada, escuchó solo en `127.0.0.1:5080` y `GET /health` respondió `{"status":"ok"}`.
- Perfil de producción sin huella de certificado: informó el requisito faltante y terminó con código 1, sin una excepción JIT.
- Prueba aislada de `CoordinatorPairingStore` sobre una base temporal: registró un dispositivo, entregó token una sola vez y rechazó reutilizar el mismo código.
- Prueba de la ruta de canje sobre el perfil HTTP local: devolvió 400 porque la vinculación exige HTTPS.
- La primera comprobación HTTPS quedó impedida por permisos del sandbox. En el subpaso de las pantallas se ejecutó con acceso autorizado al SDK/almacén, usando un certificado temporal y eliminándolo junto con su clave al terminar; el flujo completo del cliente compartido pasó.
- No se instaló como servicio persistente ni se cambió el firewall. La importación real de Windows y respaldo Android quedan pendientes antes de sincronizar; también faltan API de operaciones, autenticación compartida, revocación, hub SignalR y pruebas LAN reales.

## T7.1 — Pantallas de vinculación y cliente seguro (2026-09-29)

### Archivos creados

- `MauiApp1.Application/Lan/ICoordinatorPairingClient.cs`: contratos del cliente, almacenamiento seguro y resultados sin exponer la clave a la pantalla.
- `MauiApp1.Infrastructure/Lan/CoordinatorPairingClient.cs`: conexión HTTPS con certificado fijado por SHA-256; validación de dirección privada; generación/canje de códigos; recuperación de confirmación y persistencia previa al envío.
- `MauiApp1/Services/SecureCoordinatorLinkStorage.cs`: guarda configuración, identidad, clave y estado pendiente/confirmado juntos en SecureStorage.
- `MauiApp1/Presentation/ViewModels/DevicePairingViewModel.cs`: estados, validaciones, comandos, limpieza de contraseña y visibilidad de generación solo para Administrador en Windows.
- `MauiApp1/Presentation/Views/DevicePairingPage.xaml` y `.xaml.cs`: pantalla compartida con botón Volver, dirección, código de seguridad, generación e ingreso del código.
- `MauiApp1.Coordinator/Deployment/VINCULACION_LAN.md`: instrucciones de uso para cuando esté instalado el coordinador.

### Archivos modificados

- `MauiApp1/MauiProgram.cs`: registra cliente, almacenamiento, página y modelo de vista.
- `MauiApp1/Presentation/Views/MainPage.xaml` y `.xaml.cs`: agrega el acceso «Vinculación por red local».
- `MauiApp1.Coordinator/Program.cs` y `Pairing/CoordinatorPairingStore.cs`: reciben la clave generada/persistida por el cliente; guardan solo su hash y permiten recuperar la confirmación con prueba de posesión de esa misma clave. La respuesta deja de devolver la clave.
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md` y el plan por módulos: registran el avance y los pendientes.

### Verificación y límites

- Coordinador y aplicación Windows/Android: compilación correcta sin advertencias ni errores.
- Comprobación temporal con el cliente real de Infrastructure y un coordinador HTTPS: dirección externa y certificado incorrecto rechazados; credenciales incorrectas y Cajero no generan códigos; Administrador genera y dispositivo canjea; el mismo código no autoriza otro dispositivo.
- Al simular un fallo al guardar la confirmación local, permaneció la clave pendiente y el reintento recuperó la autorización. Una huella incorrecta no guarda una configuración bloqueada.
- Comprobaciones directas de la base temporal: otro código invalida el anterior; código vencido rechazado; un identificador de dispositivo sin su clave no recupera la autorización.
- Las bases, certificado/clave y proceso de prueba se retiraron al terminar. La prueba usa almacenamiento simulado para comprobar el protocolo; el funcionamiento visual y SecureStorage real del teléfono/Windows quedan pendientes. No se considera habilitada la sincronización de cuentas o negocio.

## T7.1 — API para sobres de sincronización (2026-09-29)

### Archivos creados

- `MauiApp1.Coordinator/Sync/CoordinatorOperationStore.cs`: almacén SQLite del diario central, con identificador de operación único, hash de contenido para reconocer reintentos idénticos y secuencia incremental para consultar cambios.
- `MauiApp1.Coordinator/Deployment/SINCRONIZACION_API.md`: contrato de los endpoints, encabezados de autorización, límites y alcance aún pendiente.

### Archivos modificados

- `MauiApp1.Coordinator/Program.cs`: agrega `POST /api/sync/operations` y `GET /api/sync/changes`; ambas requieren HTTPS y una clave de dispositivo vinculado. El envío confirma solo tras guardar la operación; la consulta pagina por cursor y excluye las operaciones originadas por el mismo dispositivo.
- `MauiApp1.Coordinator/Pairing/CoordinatorPairingStore.cs`: añade comprobación de autorización del dispositivo comparando el hash de la clave en tiempo constante.
- `D:\Trabajo\Tesis\TASK.md` y `ARCHITECTURE.md`: registran el alcance terminado y que todavía no se aplica el diario a las tablas de negocio.

### Verificación y límites

- `dotnet build MauiApp1.Coordinator/MauiApp1.Coordinator.csproj -c Debug --no-restore`: correcto, 0 advertencias y 0 errores.
- Prueba aislada del diario sobre SQLite temporal: primer guardado aceptado, reintento idéntico reconocido con la misma secuencia, contenido distinto para el mismo identificador rechazado; el cursor incluyó operaciones de otro dispositivo y excluyó las propias.
- Arranque temporal de desarrollo: migraciones centrales aplicadas y `GET /health` respondió `{"status":"ok"}`. No se pudo ejecutar una llamada de extremo a extremo a las rutas de sincronización: el entorno denegó escritura en el almacén de certificados de usuario requerido para HTTPS. Por eso, las rutas tienen verificación de compilación y revisión del código; el almacenamiento SQLite sí pasó la comprobación aislada descrita arriba.
- La base temporal y cualquier artefacto de prueba se retiraron al cerrar la comprobación.
- No se ha importado la base real ni instalado/expuesto el servicio. La API aún no es consumida por la Outbox MAUI, no aplica operaciones a las tablas de negocio, no valida la sesión/identidad central del usuario y no publica eventos SignalR; esas tareas quedan pendientes según T7.1–T7.3.
