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
- Prueba HTTPS de extremo a extremo con un certificado y base central temporales: `GET /health` devolvió estado correcto; petición anónima a cambios devolvió 401; una operación autorizada se aceptó; el reintento idéntico informó que ya estaba aceptada; mismo `OperationId` con payload distinto devolvió 409; un segundo dispositivo consultó una operación y el dispositivo de origen no recibió la suya como cambio remoto.
- Comprobación de limpieza: servicio temporal detenido, certificado temporal retirado del almacén (ninguna copia restante), base temporal y archivo PFX retirados. No se usaron ni alteraron las bases reales del negocio.
- La prueba requirió ejecutar el cliente y servidor de prueba fuera del sandbox, pues la pila TLS de Windows no podía adquirir credenciales dentro del sandbox. La verificación terminó correctamente; no queda pendiente de prueba para este ítem.

## T7.1 — Importación inicial y respaldo Android (2026-09-30)

### Origen y resultados

- Se localizó la base Windows en `C:\Users\HP Pavilioh Gaming\AppData\Local\User Name\com.companyname.mauiapp1\Data\miscelanea-ari.db`. El archivo principal conserva el WAL de la aplicación, por lo que la importación usó la SQLite Backup API para copiar la vista comprometida sin detener ni reescribir el original.
- `dotnet MauiApp1.Coordinator.dll --bootstrap-from <base-Windows> --data-directory "C:\ProgramData\Miscelanea Ari\Coordinator"` creó `miscelanea-ari-central.db` y `Backups\Windows-inicial-20260930-115011.db`. No había destino central previo.
- Android se respaldó desde el teléfono Samsung SM-S928U conectado por ADB, paquete `com.companyname.mauiapp1`, en `D:\Trabajo\Tesis\Respaldos\Android_SM-S928U_20260930_054912`. Se conservaron juntos `miscelanea-ari.db`, `miscelanea-ari.db-wal`, `miscelanea-ari.db-shm` y `MANIFIESTO.md` con hashes SHA-256. La aplicación estaba cerrada y no se escribieron archivos en el teléfono.
- `D:\Trabajo\Tesis\Respaldos\IMPORTACION_INICIAL.md` registra rutas, evidencia, conteos y límites de esta preparación.

### Verificación y límites

- `PRAGMA integrity_check` devolvió `ok` para la base Windows de origen, la base central, el respaldo fechado y la base Android respaldada.
- Los conteos de Windows coinciden en origen, central y respaldo: 1 categoría, 1 producto, 1 compra/1 renglón, 2 movimientos, 1 venta/1 renglón, 1 Administrador y 4 operaciones Outbox. La base Android tiene 1 cuenta local y todavía no tiene datos de negocio ni operaciones.
- Windows permanece como origen local y también se copia como base central. Android conserva su base y su cuenta independientes; no se reemplazó ni combinó.
- No se inició el servicio Windows, no se cambió firewall y no se habilitó sincronización. La aplicación de operaciones pendientes y la unión segura de identidades son tareas posteriores de T7.2.

## T7.1 — Instalar el coordinador como servicio Windows (2026-09-30)

### Instalación

- Se publicó `MauiApp1.Coordinator` autocontenido para `win-x64` y se instaló en `C:\Program Files\Miscelanea Ari\Coordinator`.
- Se registró `MiscelaneaAriCoordinator` como servicio Windows con inicio `Automatic`, ejecutándose como `LocalSystem`, URL HTTPS `https://0.0.0.0:5080` y la base central bajo `C:\ProgramData\Miscelanea Ari\Coordinator`.
- Se generó un certificado de servidor en `LocalMachine\My`; su huella SHA-256 de 64 caracteres se guardó en `coordinator-certificate-fingerprint.txt` para el emparejamiento futuro.
- Se creó la regla TCP 5080 vinculada al ejecutable instalado, solo entrada y solo perfil `Private`.

### Verificación y límites

- Estado del servicio: `Running`; configuración SCM: `AUTO_START`. `GET https://127.0.0.1:5080/health` devolvió `{"status":"ok"}`. Se comprobó que la regla está habilitada, es `Private`, usa TCP 5080 y corresponde al ejecutable instalado.
- La Wi-Fi actual `CLARO_hU5Us7` permanece con perfil `Public`; no se cambió la clasificación de red. El firewall, por diseño, bloquea clientes entrantes por esa red hasta que el usuario confirme que es la red confiable del negocio y cambie su perfil a `Private`. Aún no se ha probado conexión LAN desde Android.
- El instalador requiere PowerShell 7 o posterior y privilegios de Administrador; `install-coordinator.ps1` ahora verifica la versión antes de comenzar.
- No se configuró reenvío de puertos ni se expuso el servicio a Internet. La sincronización de negocio sigue pendiente de T7.2/T7.3.
- La base central ya fue importada y el servicio ya está instalado; los pasos posteriores de este mismo historial registran ambas verificaciones. La API todavía no es consumida por la Outbox MAUI ni aplica operaciones a las tablas de negocio (T7.2), y aún no publica eventos SignalR (T7.3).

## T7.1 — Indicador de disponibilidad del coordinador (2026-09-30)

### Archivos modificados

- `MauiApp1.Application/Lan/ICoordinatorPairingClient.cs`: agrega el resultado y contrato de comprobación de disponibilidad.
- `MauiApp1.Infrastructure/Lan/CoordinatorPairingClient.cs`: consulta `/health` con la dirección y huella HTTPS ya guardadas, respetando la validación de certificado y el límite de espera del cliente; maneja desconexión, timeout o respuesta inválida como no disponible.
- `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs`: representa estados sin configuración, coordinador disponible, vinculación pendiente y falta de respuesta; informa que el trabajo local continúa disponible.
- `MauiApp1/Presentation/Views/MainPage.xaml` y `.xaml.cs`: muestra el estado en Inicio y lo actualiza cuando la pantalla reaparece.
- `ARCHITECTURE.md`: documenta el comportamiento y aclara que `/health` no significa que los datos estén sincronizados.
- `D:\Trabajo\Tesis\TASK.md`: marca completado este ítem de T7.1.

### Verificación y límites

- Aplicación MAUI Windows (`net10.0-windows10.0.19041.0`): compilación exitosa, 0 advertencias y 0 errores.
- Aplicación MAUI Android (`net10.0-android`): compilación exitosa, 0 advertencias y 0 errores.
- El indicador es de solo lectura y no bloquea operaciones locales. Cuando no existe vínculo, muestra que el coordinador no está configurado en esa instalación; cuando existe, verifica el proceso remoto usando la huella fijada. La validación visual y de pérdida real de LAN en Android queda para una comprobación con el dispositivo del usuario.

## T7.1 — Sesiones centrales de usuario y atribución segura (2026-09-30)

### Archivos modificados

- `MauiApp1.Coordinator/Pairing/CoordinatorPairingStore.cs`: crea sesiones centrales ligadas a dispositivo/usuario, guarda solo hash SHA-256 de un token aleatorio, establece expiración de 12 horas y rechaza sesiones vencidas, de otro dispositivo o cuya cuenta ya esté deshabilitada.
- `MauiApp1.Coordinator/Program.cs`: agrega `POST /api/auth/session`; protege la consulta y recepción de operaciones con token de dispositivo y sesión de usuario; controla solicitudes por HTTPS, cuenta habilitada y límite de intentos.
- `MauiApp1.Coordinator/Sync/CoordinatorOperationStore.cs`: registra el usuario autenticado de la sesión, no el `ActorUserId` no confiable enviado en el cuerpo.
- `MauiApp1.Coordinator/Deployment/SINCRONIZACION_API.md`: describe el contrato, vigencia, encabezados y límite de la sesión.
- `MauiApp1.Coordinator/Deployment/update-coordinator.ps1`: actualiza el servicio con copia previa de SQLite vía Backup API y de los binarios, verificación local de `/health` y reversión del ejecutable si falla.
- `ARCHITECTURE.md`: documenta la autenticación central disponible y que todavía no se integra al inicio de sesión MAUI.
- `D:\Trabajo\Tesis\TASK.md`: marca la autenticación de API y las reglas vigentes de dispositivo/usuario; mueve la integración MAUI, resolución de cuentas entre bases y conjunto de datos compartido al trabajo de sincronización T7.2.

### Verificación y límites

- `dotnet build MauiApp1.Coordinator/MauiApp1.Coordinator.csproj -c Debug --no-restore`: correcto, 0 advertencias y 0 errores.
- Sesiones sobre SQLite temporal: token correcto aceptado solo para su dispositivo; token incorrecto y otro dispositivo rechazados; al deshabilitar la cuenta central se invalida la sesión. Los datos de prueba se retiraron.
- Publicación `Release` autocontenida `win-x64`: correcta. El script actualizador pasó análisis sintáctico de PowerShell.
- Se creó una copia coherente de la base central en `C:\ProgramData\Miscelanea Ari\Coordinator\Backups\Pre-T7.1-Upgrade-20260930` y copia de binarios anteriores en su subcarpeta `CoordinatorBin`.
- La sesión de desarrollo no pudo detener el servicio por carecer de privilegio administrativo; no se reemplazó el binario instalado. El servicio siguió `Running` y `/health` respondió `{"status":"ok"}` desde la comprobación local autorizada. El actualizador preparado debe ejecutarse como Administrador antes de consumir la API nueva.
- La ruta del servidor queda definida; el flujo MAUI aún no solicita esta sesión ni envía su token. La vinculación visual/SecureStorage y la integración de usuarios en ambos dispositivos deben confirmarse antes de cerrar T7.1; las operaciones de negocio compartidas corresponden a T7.2.
- La política de respaldo y recuperación aún requiere acuerdo antes de marcar T7.1 completo.

## T7.1 — Separar LAN de Internet y validar modo sin ISP (2026-09-30)

### Archivos modificados

- `MauiApp1/MauiProgram.cs`: registra `IConnectivity` para consultar el estado de red de la plataforma.
- `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs`: traduce `Internet`, `ConstrainedInternet`, `Local`, `None` y `Unknown` en mensajes separados del estado LAN del coordinador.
- `MauiApp1/Presentation/Views/MainPage.xaml` y `.xaml.cs`: presenta ambos estados y actualiza el indicador al cambiar la conectividad mientras Inicio está visible.
- `ARCHITECTURE.md`: documenta qué comprueba cada estado y el resultado de la revisión de dependencias de red.
- `D:\Trabajo\Tesis\TASK.md`: marca las comprobaciones de LAN frente a ISP y de funcionamiento local sin servicios externos.

### Verificación y límites

- .NET MAUI define `NetworkAccess.Local` como acceso a red local solamente y `Internet` como acceso local e Internet; la app presenta esa lectura del sistema operativo separada de la consulta directa al coordinador. El dato del sistema operativo es indicativo: la disponibilidad real del coordinador se decide por su respuesta HTTPS fijada.
- Búsqueda del código de red en todos los proyectos MAUI/Application/Infrastructure/Coordinator: un solo `HttpClient`, dirigido a la IP privada configurada y con huella fijada; no hay endpoints de nube o ISP. El resto de los servicios de negocio persiste en SQLite local.
- La prueba de funcionamiento LAN durante una caída real del ISP queda pendiente de desconectar solo el acceso WAN y observar los dispositivos en el local; no se marca como prueba física.
- Aplicación MAUI Windows: build correcto, 0 advertencias y 0 errores. Aplicación MAUI Android: build correcto, 0 advertencias y 0 errores.
- Se instaló la actualización Android con `adb install -r` en el Samsung SM-S928U conectado, usando actualización que preserva los datos de la app. No se ingresaron credenciales ni se modificó la base local. La revisión visual de Inicio y SecureStorage aún debe confirmarse en el teléfono.

## T7.1 — Facilitar vinculación mediante QR temporal (2026-09-30)

### Archivos modificados

- `MauiApp1/MauiApp1.csproj`: incorpora `ZXing.Net.Maui.Controls` para generar QR y leerlos con cámara en Android.
- `MauiApp1/MauiProgram.cs`: inicializa el lector de códigos.
- `MauiApp1/Platforms/Android/AndroidManifest.xml`: declara permiso de cámara; Android solicita aprobación al abrir el escáner.
- `MauiApp1/Presentation/ViewModels/DevicePairingViewModel.cs`: genera payload versionado con dirección, huella, código de un uso y vencimiento; valida el contenido leído y rellena los campos sin vincular automáticamente.
- `MauiApp1/Presentation/Views/DevicePairingPage.xaml` y `.xaml.cs`: muestra el QR en Windows e inicia el lector en Android.
- `MauiApp1/Presentation/Views/PairingQrScannerPage.xaml` y `.xaml.cs`: vista de escaneo con manejo de permiso/lectura y retorno al formulario.
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`: documentan la seguridad y el estado de validación.

### Verificación y límites

- Windows y Android compilaron correctamente con 0 advertencias y 0 errores.
- La actualización firmada se instaló en Samsung SM-S928U con `adb install -r`, preservando los datos existentes, y se abrió la aplicación.
- Falta que el usuario pruebe la lectura del QR desde la cámara y confirme el mensaje de vinculación. El escaneo físico aún no se da por verificado.

## T7.1 — Verificar reapertura de vinculación en Android 16 (2026-09-30)

### Resultado

- El teléfono cerraba la aplicación al entrar a la pantalla después del despliegue con el lector QR. El registro fatal indicaba que Android intentaba reanudar una raíz de navegación cuyo contenedor ya no estaba presente (`NavigationRootManager_ElementBasedFragment`).
- Se alineó el proyecto con la versión MAUI administrada por la carga instalada de Visual Studio; se retiró el override temporal a una versión NuGet más nueva que no correspondía a esa carga.
- Se reconstruyeron Windows y Android. Ambos destinos compilaron sin advertencias ni errores.
- Se actualizó el Samsung SM-S928U mediante instalación de actualización, sin borrar la base ni los datos de la app. El inicio permaneció abierto; al tocar «Vinculación por red local» la pantalla cargó sin cerrar la app y mostró «Este dispositivo está vinculado».
- No se generó ni escaneó QR durante esta verificación porque este teléfono ya está vinculado. La lectura de cámara desde otro dispositivo sin vincular queda pendiente.
