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

## T7.1 — Desvinculación segura del dispositivo (2026-09-30)

### Cambios realizados

- `MauiApp1.Application/Lan/ICoordinatorPairingClient.cs`: añade las operaciones para desvincular y eliminar las credenciales de vinculación local.
- `MauiApp1/Services/SecureCoordinatorLinkStorage.cs`: elimina solo la entrada de SecureStorage correspondiente a la vinculación; no toca SQLite de negocio ni el identificador del dispositivo.
- `MauiApp1.Infrastructure/Lan/CoordinatorPairingClient.cs`: llama al coordinador con HTTPS y certificado fijado, autentica la solicitud con el token del dispositivo y borra SecureStorage solo tras confirmación. Ante fallo de red conserva credenciales y muestra cómo reintentar.
- `MauiApp1.Coordinator/Pairing/CoordinatorPairingStore.cs`: revoca autorización y sesiones ligadas al dispositivo en una sola transacción, comparando de forma constante el hash SHA-256 del token.
- `MauiApp1.Coordinator/Program.cs`: añade `POST /api/devices/unlink`, limitado al dispositivo autenticado del mismo token; admite reintentos seguros tras una respuesta perdida.
- `MauiApp1/Presentation/ViewModels/DevicePairingViewModel.cs`, `Presentation/Views/DevicePairingPage.xaml` y `.xaml.cs`: botón visible al estar vinculado y confirmación que explica la conservación de los datos locales.
- `ARCHITECTURE.md` y `D:\Trabajo\Tesis\TASK.md`: documentan el comportamiento y dejan pendiente la confirmación manual en ambos destinos.

### Verificación y límites

- `MauiApp1.Coordinator` compila con 0 advertencias y 0 errores.
- La app MAUI compila para Windows y Android con 0 advertencias y 0 errores.
- La desvinculación aún no se probó con el servicio real ni en teléfono: el servicio Windows instalado requiere actualizarse como Administrador para exponer `POST /api/devices/unlink`. Hasta entonces, el botón de la app nueva no podrá completar la revocación con el coordinador actualmente instalado.

## T7.1 — Hacer visible el resultado de desvincular (2026-09-30)

- Tras el primer intento en Android se confirmó que la pantalla seguía indicando «vinculado»: no se debe borrar el token local ni cambiar el estado hasta que el coordinador confirme la revocación.
- `MauiApp1.Infrastructure/Lan/CoordinatorPairingClient.cs`: ahora identifica la respuesta 404 como servicio coordinador sin la nueva función y explica que la vinculación se conserva.
- `MauiApp1/Presentation/ViewModels/DevicePairingViewModel.cs` y `Presentation/Views/DevicePairingPage.xaml.cs`: el intento se espera y siempre presenta un diálogo final explícito de éxito o fallo, además del estado persistente de vinculación.
- Verificación: Windows y Android compilaron con 0 advertencias y 0 errores. La revocación real continúa pendiente porque el servicio Windows todavía debe actualizarse como Administrador; después se repetirá la prueba física.

## T7.1 — Preparar publicación compatible con el Windows del negocio (2026-09-30)

- El primer intento de actualización del servicio creó correctamente el respaldo y luego falló al iniciar el nuevo binario: el equipo tiene disponible .NET x86 10.0.9, pero no el runtime x64 requerido por una publicación dependiente. El actualizador revirtió los binarios y el registro de eventos confirmó que el servicio volvió a iniciar.
- Se republicó `MauiApp1.Coordinator` como `win-x64 --self-contained true`; se verificó la presencia de `hostfxr.dll`, `hostpolicy.dll` y `coreclr.dll` en la publicación.
- `MauiApp1.Coordinator/Deployment/update-coordinator.ps1` ahora valida esos componentes antes de respaldar o detener el servicio, para evitar repetir una actualización con publicación incompleta.
- La nueva actualización aún debe ejecutarse desde PowerShell 7 como Administrador. El script conservará un respaldo previo y comprobará el estado del servicio y `/health` antes de anunciar éxito.
- Ejecución completada correctamente por el usuario el 2026-09-30. Respaldo: `C:\ProgramData\Miscelanea Ari\Coordinator\Backups\PreUpdate-20260930-083716`. Se comprobó `/health` (`ok`) y que `POST /api/devices/unlink` responde 401 sin credenciales, lo que confirma que la ruta está activa y protegida.
- Se instaló con `adb install -r` la APK actualizada en el Samsung SM-S928U conectado, conservando datos y vínculo para la prueba final del usuario.
- El 2026-10-01 el usuario confirmó que la desvinculación funcionó correctamente en Android. El registro de tareas separa esta validación de la prueba Windows pendiente; el teléfono queda listo para probar el QR de una nueva vinculación.
- El 2026-10-01 el usuario probó la lectura de QR en el Samsung SM-S928U y confirmó que la app rellena automáticamente los datos para vincularse con Windows. La revisión visual de Windows queda pendiente.
- El 2026-10-01 el usuario aclaró que Windows opera solo como coordinador y no está vinculado como cliente. Se registra como no aplicable la prueba de desvinculación de la instalación Windows; no se vinculará artificialmente al host consigo mismo.
- El 2026-10-01 el usuario aprobó utilizar una memoria USB como medio externo y no usar nube; acordó respaldos diarios, con retención de 30 días y copia adicional antes de actualizaciones. T7.4 implementará y verificará el procedimiento de respaldo/restauración.

## T7.2 — Inicio de sesión central con acceso local de respaldo (2026-10-01)

### Cambios realizados

- `MauiApp1.Application/Authentication/ICoordinatedSignInService.cs`: contratos para el flujo central, estados de coordinador no vinculado/no disponible/rechazo y sesión central.
- `MauiApp1.Application/Authentication/IAuthenticationService.cs` y `MauiApp1.Infrastructure/Authentication/LocalAuthenticationService.cs`: agregan asociación central-local; tras login central correcto reutilizan la cuenta local que conserva historiales o crean su proyección local, actualizan su rol y guardan un verificador PBKDF2 offline.
- `MauiApp1.Infrastructure/Persistence/Entities/UserAccountEntity.cs`, `LocalDbContext.cs`, `MauiApp1.Infrastructure/Persistence/Migrations/20261001090000_CoordinatorUserMapping.cs` y `LocalDbContextModelSnapshot.cs`: añaden `CoordinatorUserId` nullable/único sin cambiar identificadores ni historiales existentes.
- `MauiApp1.Infrastructure/Lan/CoordinatorPairingClient.cs`: solicita sesión a `/api/auth/session` por HTTPS con pin de certificado y distingue rechazo de credenciales de falta de respuesta.
- `MauiApp1.Infrastructure/Authentication/CoordinatedSignInService.cs` y `MauiApp1/Services/SecureCoordinatorUserSessionStorage.cs`: usan acceso central cuando está disponible; permiten acceso local si no responde el coordinador y mantienen solo sesiones locales-central coincidentes. El token se guarda en SecureStorage y se elimina al desvincular.
- `MauiApp1/Presentation/ViewModels/AccessPageViewModel.cs` y `Presentation/Views/AccessPage.xaml`: conectan el nuevo flujo y muestran aviso para el acceso local offline.
- `MauiApp1/MauiProgram.cs`: registra el cliente compartido, almacenamiento seguro y servicio de acceso.
- `MauiApp1/Presentation/Views/AccessPage.xaml`: actualiza el texto de configuración para explicar el inicio central y el acceso local cuando el coordinador no responde.
- `MauiApp1.Infrastructure/Lan/CoordinatorPairingClient.cs`: valida explícitamente un token de sesión vacío como respuesta inválida, evitando una excepción inesperada ante una respuesta incompleta del servidor.

### Verificación y límites

- `MauiApp1.Coordinator`, la app Windows y Android compilaron con 0 advertencias y 0 errores.
- Después de los dos ajustes finales, Windows volvió a compilar correctamente (0 advertencias, 0 errores), y Android también (0 advertencias, 0 errores).
- La comprobación automatizada temporal sobre SQLite no llegó a ejecutarse: el entorno bloqueó la lectura de `NuGet.Config` del perfil del usuario durante la restauración. No se modificaron bases de datos reales. Por eso, la migración y los flujos central/offline aún requieren una prueba funcional en dispositivos.

## T7.2.2 — Atribución de operaciones Outbox mediante sesión central (2026-10-01)

### Cambios realizados

- `MauiApp1.Application/Sync/ICoordinatorOperationSender.cs`: define el contrato y los resultados explícitos de aceptación, operación ya aceptada, falta de vínculo/sesión, discrepancia de identidad, rechazo y desconexión.
- `MauiApp1.Infrastructure/Lan/CoordinatorOperationSender.cs`: prepara el envío HTTPS de una operación Outbox con credenciales de dispositivo y sesión de usuario central; verifica que operación, usuario local activo, mapeo local-central y dispositivo sean coherentes. Rechaza el envío si una de esas identidades no coincide.
- La solicitud envía el ID central asociado en el campo histórico `actorUserId`, pero el coordinador sigue derivando el actor exclusivamente desde `X-User-Session`. El cliente solo considera aceptada la operación si recibe una respuesta válida con secuencia.
- `MauiApp1/MauiProgram.cs`: registra el remitente para los pasos que procesarán la cola Outbox.
- `MauiApp1.Coordinator/Deployment/SINCRONIZACION_API.md`: actualiza el estado real de despliegue del servicio Windows.
- `TASK.md` y `ARCHITECTURE.md`: registran el alcance y diferencian el remitente del worker que procesará y marcará operaciones confirmadas.

### Verificación y límites

- Aplicación Windows y Android compiladas después de los cambios: 0 advertencias y 0 errores en ambos destinos.
- El coordinador ya deriva el actor de la sesión validada y lo persiste en la tabla del diario; ese servidor fue desplegado en Windows y su API se probó previamente con operaciones durables e idempotentes.
- Aún no se procesa automáticamente la cola ni se marca una operación confirmada; eso corresponde a los siguientes subpasos de T7.2. Tampoco se instaló ni probó este cliente físicamente en Android en esta sesión.

## T7.2.3 — Resolver cuentas existentes con Windows como origen (2026-10-01)

### Decisión y comportamiento

- Windows y la base que se importó a `C:\ProgramData\Miscelanea Ari\Coordinator\miscelanea-ari-central.db` son la fuente inicial de las cuentas y de los datos del negocio.
- Después de que el coordinador valida la contraseña, MAUI busca primero una asociación por `CoordinatorUserId`. Si no la hay, puede asociar una cuenta local con el mismo nombre normalizado. Conserva el `UserId` local para no romper referencias históricas; conflicto de identidad o asociación previa diferente se rechaza sin guardar.
- La asociación de cuenta no copia ni fusiona categorías, productos, ventas, compras, inventario, clientes ni pagos. La base local Android permanece en el dispositivo y sus datos no se incorporan automáticamente a Windows.

### Evidencia y límites

- `Respaldos/IMPORTACION_INICIAL.md` y `Respaldos/Android_SM-S928U_20260930_054912/MANIFIESTO.md`: Windows conserva 1 cuenta y los datos de negocio importados; Android conserva 1 cuenta local y 0 registros de negocio/Outbox. Integridad SQLite verificada `ok` durante la importación.
- El mapeo seguro ya se implementó en T7.2.1 (`LocalAuthenticationService.SignInWithCoordinatorUserAsync`) y las compilaciones Windows/Android de T7.2.2 pasaron con 0 advertencias y 0 errores; aquí se formaliza y documenta la política para no confundir enlace de identidad con fusión de datos.
- La cola todavía no procesa operaciones automáticamente y las pantallas aún trabajan con SQLite local. La aplicación central duradera y la lectura de los datos compartidos siguen pendientes en los siguientes subpasos; la sincronización funcional no se declara terminada.
- Se intentó actualizar la app en el Samsung SM-S928U, pero ADB informó que no hay dispositivos conectados. La instalación física, migración de la base existente y pruebas central/offline quedan pendientes; este subpaso no se marca verificado en dispositivo hasta que el usuario conecte el teléfono.
- Las operaciones del Outbox aún no consumen la sesión central; la atribución/envío sigue en los pasos posteriores de T7.2.

## T7.2.4–T7.2.9 — Cola, aplicación central, recepción y conflictos (2026-10-01)

### Cambios

- `MauiApp1.Infrastructure/Sync/CoordinatorBusinessOperationApplier.cs`: centraliza la aplicación transaccional de las operaciones de compra, venta, inventario, categoría, producto, cliente y pago. Conserva compatibilidad con compras sin snapshot histórico; las ventas sin existencia registran faltante sin producir stock negativo. Las ediciones de catálogo comparan la versión esperada y devuelven conflicto si cambió en otro dispositivo.
- `MauiApp1.Coordinator/Sync/CoordinatorOperationStore.cs` y `MauiApp1.Coordinator/Program.cs`: guardan el diario y el resultado de negocio en una transacción; distinguen cambios aplicados de conflictos; entregan solo cambios aplicados a clientes; permiten que un Administrador consulte y resuelva conflictos de producto/categoría, dejando un evento de resolución durable.
- `MauiApp1.Infrastructure/Lan/LocalOperationSyncService.cs`: procesa el Outbox en orden, guarda confirmación solo tras acuse válido, conserva conflictos, descarga cambios con cursor durable, crea proyecciones de actores y presenta conflictos que requieren decisión antes de avanzar el cursor. Cuando MAUI Windows comparte la base central, publica las operaciones Outbox locales al diario central de forma transaccional, dado que el cambio comercial ya está persistido allí.
- `MauiApp1.Application/Sync`, `MauiApp1.Application/Authentication` y `MauiApp1.Infrastructure/Lan/CoordinatorPairingClient.cs`: contratos de envío/recepción/revisión y alta de cajeros por LAN con la autoridad del coordinador.
- `MauiApp1.Infrastructure/Persistence/Entities/DeviceSyncStateEntity.cs`, `DeviceSyncConflictEntity.cs`, `InventoryDiscrepancyEntity.cs`, `MauiApp1.Infrastructure/Persistence/Migrations/20261001160000_InventoryDiscrepancies.cs` y `LocalDbContextModelSnapshot.cs`: cursor por dispositivo, conflictos entrantes y faltantes de inventario.
- `MauiApp1/Presentation/Views/ConflictReviewPage.cs`, `MainPage.xaml` y `MainPageViewModel.cs`: acceso de Administrador a la revisión de conflictos e indicador de pendientes, conflictos y última sincronización.
- `MauiApp1.Coordinator/Deployment/SINCRONIZACION_API.md`, `ARCHITECTURE.md` y `TASK.md`: estado del protocolo y trazabilidad del avance.

### Verificación y límites

- El coordinador, la app Windows y la app Android compilaron con cero advertencias y cero errores.
- Prueba SQLite aislada completada: migración, actualización transaccional de producto, venta que excede existencias con discrepancia registrada, payload legado de compra con snapshots completados y rechazo de cambio concurrente.
- No se alteraron bases de datos reales. La ventana emergente de `dotnet.exe` observada durante la comprobación provino del cierre inicial del arnés temporal; se corrigió su limpieza y la ejecución posterior terminó en `PASS` sin excepción.
- La implementación de T7.2 queda registrada; su aceptación de operación en dos equipos (incluidos el teléfono físico, reconexión offline y lectura desde otro cliente) sigue pendiente de instalación y recorrido manual. T7.3 añadirá actualización en vivo con SignalR.

### Diagnóstico de la primera prueba LAN (2026-10-01)

- La base central en `C:\ProgramData\Miscelanea Ari\Coordinator\miscelanea-ari-central.db` pasó `PRAGMA integrity_check`, pero `CoordinatorOperations` aún no tenía las columnas `Status` y `ConflictMessage` que agrega el código actual. Esto confirma que el servicio instalado corre una versión anterior; explica «No se pudo actualizar la cola» en Windows y que Android no reciba el catálogo. «Coordinador sin configurar» en la app Windows es normal porque ese equipo aloja el coordinador y no se vincula como cliente.
- Se preparó la publicación autónoma actualizada en `MauiApp1.Coordinator/bin/publish/win-x64`; incluye ejecutable y runtime requeridos por `update-coordinator.ps1`. El usuario aún debe ejecutar el actualizador desde PowerShell 7 o posterior como Administrador para tomar el respaldo, instalar la publicación y aplicar la migración. La verificación con dos dispositivos queda pendiente hasta esa actualización.

### Corrección de navegación de la revisión de conflictos (2026-10-01)

- `MauiApp1/Presentation/Views/ConflictReviewPage.cs`: añade «← Volver al menú principal» al inicio de la vista modal y conserva ese encabezado al actualizar la lista de conflictos. El botón cierra la vista y regresa al menú desde el que se abrió.
- Verificación: compilaciones de Windows y Android correctas, sin advertencias ni errores. La pulsación real en el teléfono queda para la comprobación manual de T7.2.

### Corrección del inicio de Windows con la base central (2026-10-01)

- `MauiApp1.Infrastructure/Persistence/LocalDatabaseInitializer.cs`: permite que la aplicación verifique migraciones pendientes cuando las administra un proceso externo, y entrega un mensaje claro si la base central aún no fue actualizada o no tiene identidad inicializada.
- `MauiApp1.Infrastructure/Persistence/LocalPersistenceServiceCollectionExtensions.cs`: incorpora el indicador para elegir quién administra las migraciones.
- `MauiApp1/MauiProgram.cs`: cuando Windows detecta la base central del coordinador, evita ejecutar migraciones/escrituras desde la interfaz. Android y bases locales continúan aplicándolas normalmente; el servicio coordinador sigue siendo responsable de migrar la base central.
- Causa del error reportado: la app Windows intentaba obtener el bloqueo de migraciones SQLite sobre una base central que debe administrar el servicio. El esquema instalado también estaba desactualizado, por lo que debe ejecutarse `update-coordinator.ps1` antes de abrir con una versión que requiera la última migración.
- Verificación: compilaciones de Windows, Android y Coordinador correctas, sin advertencias ni errores. Falta confirmar el arranque en el Windows del usuario después de actualizar el servicio coordinador.

### Diagnóstico visible de operaciones pendientes (2026-10-01)

- `MauiApp1.Infrastructure/Lan/LocalOperationSyncService.cs`: cuando el coordinador no acepta una operación y esta debe reintentarse, conserva un motivo legible en la cola local y lo incluye en el estado de sincronización.
- `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs`: presenta ese texto como detalle de sincronización; ya no lo etiqueta erróneamente como un conflicto.
- Esto permite distinguir una operación no enviada (pendiente) de un conflicto real. La vista «Revisión de conflictos» correctamente puede estar vacía aunque haya operaciones pendientes.
- Verificación: compilaciones de Android y Windows correctas, sin advertencias ni errores. Falta instalar la nueva app Android y revisar el detalle que muestre al reintentar las operaciones pendientes.

### Reintento manual y diagnóstico de autorización (2026-10-01)

- `MauiApp1/Presentation/Views/MainPage.xaml` y `MainPage.xaml.cs`: añade el botón «Sincronizar ahora», con estado visible mientras se ejecuta.
- `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs`: impide reintentos simultáneos y aclara en Windows que el equipo anfitrión no necesita vincularse como cliente.
- `MauiApp1/Presentation/Views/ConflictReviewPage.cs`: explica la diferencia entre conflictos recibidos y operaciones pendientes, muestra el total pendiente y permite reintentar.
- `MauiApp1.Infrastructure/Sync/CoordinatorConflictReviewService.cs`: una consulta remota fallida ya no aparece falsamente como lista vacía.
- `MauiApp1.Coordinator/Program.cs`, `MauiApp1.Infrastructure/Lan/CoordinatorOperationSender.cs` y `MauiApp1.Infrastructure/Lan/LocalOperationSyncService.cs`: devuelve y presenta el motivo específico cuando el coordinador rechaza el dispositivo o su sesión.
- Verificación: compilaciones de Android, Windows y Coordinador correctas, sin advertencias ni errores. Publicación autónoma del Coordinador preparada en `MauiApp1.Coordinator/bin/publish/win-x64`. La prueba en ambos dispositivos queda pendiente de instalar la app Android/Windows y actualizar el servicio.

### Limpieza de sesión compartida al cerrar sesión (2026-10-01)

- `MauiApp1.Application/Authentication/ICoordinatedSignInService.cs` y `MauiApp1.Infrastructure/Authentication/CoordinatedSignInService.cs`: el cierre de sesión elimina también el token de sesión compartida almacenado en forma segura.
- `MauiApp1.Infrastructure/Lan/CoordinatorOperationSender.cs`: al recibir HTTP 401 invalida el token compartido local para evitar reintentos con una credencial rechazada.
- `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs`: el botón «Cerrar sesión» usa ahora el cierre coordinado.
- Causa observada: el usuario salía de la sesión local, pero el token central vencido seguía almacenado. Al volver a iniciar sesión sin una sesión central nueva, el sincronizador enviaba el token anterior y seguía recibiendo 401.
- Verificación: compilación Android correcta sin advertencias ni errores; Windows compilado en `bin/BuildVerify` con 0 advertencias y 0 errores porque la app en ejecución bloqueaba la carpeta normal. La instalación y la sincronización entre dispositivos quedan pendientes.

### Manejo claro del límite de intentos al iniciar sesión (2026-10-02)

- `MauiApp1.Coordinator/Program.cs`: el limitador por IP devuelve HTTP 429, el texto de espera y `Retry-After: 60` cuando supera diez solicitudes por minuto.
- `MauiApp1.Application/Authentication/ICoordinatedSignInService.cs` y `MauiApp1.Infrastructure/Lan/CoordinatorPairingClient.cs`: representan el bloqueo temporal por separado y conservan el mensaje HTTP del coordinador.
- `MauiApp1.Infrastructure/Authentication/CoordinatedSignInService.cs`: no permite que un bloqueo temporal de acceso se confunda con un inicio local exitoso.
- Causa probable del síntoma observado: después de varios reintentos, el limitador puede responder temporalmente; el código anterior trataba esa respuesta como coordinador no disponible y permitía acceso local sin sesión compartida. La causa final se confirmará con el mensaje de acceso nuevo o con la siguiente sincronización.
- Verificación: compilación Android, Windows aislado y Coordinador correctas, todas sin advertencias ni errores. Publicación actualizada del Coordinador preparada en `MauiApp1.Coordinator/bin/publish/win-x64`; pruebas reales en Android/Windows aún pendientes.

### Mostrar el resultado del acceso central en el menú (2026-10-02)

- `MauiApp1.Application/Authentication/ICoordinatedSignInService.cs` y `MauiApp1.Infrastructure/Authentication/CoordinatedSignInService.cs`: conservan el resultado del último acceso central/local y distinguen sesión activa, acceso local, falta de vinculación y rechazo temporal.
- `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs`: muestra ese resultado en el estado de sincronización para que el acceso local de respaldo no parezca una sesión central válida.
- Motivo: la captura mostraba una cola pendiente con ausencia de sesión compartida, pero no exponía si el inicio de sesión había sido local o central.
- Verificación: compilaciones Android y Windows correctas, sin advertencias ni errores. La prueba con la nueva app aún está pendiente.

### Verificar persistencia y diagnóstico de sesión para el envío (2026-10-02)

- `MauiApp1.Infrastructure/Authentication/CoordinatedSignInService.cs`: vuelve a leer y compara la sesión segura inmediatamente después de guardarla; no informa sesión central activa si el almacenamiento no la conserva.
- `MauiApp1.Infrastructure/Lan/CoordinatorOperationSender.cs`: distingue sesión ausente, vencida o con datos/token inválidos; actualiza el estado visible si el coordinador rechaza la sesión.
- `MauiApp1.Infrastructure/Lan/LocalOperationSyncService.cs`: usa el motivo específico para las operaciones pendientes.
- Verificación: compilaciones Android y Windows correctas, 0 advertencias y 0 errores. La prueba física de lectura/escritura de SecureStorage y el reintento LAN quedan pendientes de instalar la nueva app Android.

### Corrección y autoverificación de sesión central (2026-10-02)

- `MauiApp1.Coordinator/Pairing/CoordinatorPairingStore.cs`: valida la sesión buscando primero la huella del token, compara los GUID de dispositivo/usuario como GUID y confirma que la cuenta correspondiente siga habilitada. Evita que una diferencia de formato de GUID en SQLite invalide una sesión correcta.
- `MauiApp1.Coordinator/Program.cs`: después de crear una sesión, el coordinador intenta validarla antes de responder éxito; si no puede, registra el diagnóstico y devuelve un error explícito en vez de entregar una sesión que luego será rechazada.
- `MauiApp1.Infrastructure/Lan/CoordinatorPairingClient.cs`: muestra el mensaje JSON del coordinador cuando el inicio de sesión central falla, para que un acceso local de respaldo no parezca sincronizado.
- Verificación: Coordinador Release y app MAUI Windows compilados con 0 advertencias y 0 errores. La compilación Android quedó impedida en este entorno porque el sandbox bloquea la lectura de `C:\Users\HP Pavilioh Gaming\AppData\Local\Android\Sdk`; la dependencia compartida de sincronización sí compiló con el Coordinador. Se actualizaron los ensamblados Release de la publicación autónoma ya preparada en `MauiApp1.Coordinator/bin/publish/win-x64`; la restauración/publicación completa no pudo leer el `NuGet.Config` del perfil de usuario. La sesión real y el envío de las tres operaciones aún requieren instalar los binarios actualizados y probar en el teléfono.

### Diagnóstico del cambio de categoría desde Windows (2026-10-02)

- `MauiApp1.Infrastructure/Categories/EfCategoryService.cs`: el registro de renombrado y guardado de la operación Outbox ahora queda dentro del mismo manejo de errores. Se registra la excepción completa y, en Debug, la pantalla muestra su mensaje interno para distinguir permisos, bloqueo de SQLite, restricción de datos u otro fallo; Release conserva un mensaje general.
- Motivo: la captura mostraba que Android ya sincroniza hacia Windows, pero al renombrar en Windows aparecía el mensaje genérico del ViewModel. El diagnóstico apunta a una excepción durante la preparación/guardado local que el servicio no estaba capturando. Aún no se declara corregido el guardado hasta identificar el detalle real en Windows.
- Verificación: app MAUI Windows Debug y Coordinador Release compilaron con 0 advertencias y 0 errores. La reproducción contra la instalación del usuario queda pendiente de abrir la versión actualizada de Windows y repetir el mismo cambio; si falla, el mensaje Debug ahora debe mostrar la causa concreta.

### Permisos de escritura para SQLite en Windows (2026-10-02)

- Evidencia: la app podía leer la base central, pero SQLite devolvía error 8 al editar una categoría. El archivo principal no tenía atributo de solo lectura; los auxiliares `miscelanea-ari-central.db-wal` y `-shm`, creados bajo el servicio, solo daban lectura al grupo Usuarios. SQLite necesita escribir en ellos y crear/reemplazar esos archivos.
- `MauiApp1.Coordinator/Deployment/update-coordinator.ps1` y `install-coordinator.ps1`: conceden permiso `Modify` a la cuenta de Windows de la interfaz MAUI (por defecto, la cuenta que ejecuta PowerShell; se puede indicar `-WindowsAppUser`). Se aplica a la carpeta de datos con herencia para archivos futuros y a la base/WAL/SHM existentes, sin conceder escritura a todos los usuarios.
- `MauiApp1.Coordinator/Deployment/SINCRONIZACION_API.md`: documenta el permiso necesario y el parámetro de cuenta.
- Verificación: ambos scripts pasan el analizador de PowerShell sin errores; una comprobación ACL aislada confirmó escritura para archivos SQLite existentes y herencia de permiso para un futuro WAL. El servicio real aún debe actualizarse y el renombrado repetirse en Windows para confirmar el arreglo en la instalación del usuario.

### T7.3 Avisos de cambios en vivo con SignalR (2026-10-03)

- `MauiApp1.Coordinator/Sync/CoordinatorChangesHub.cs`: agrega el hub HTTPS `/hubs/changes`, valida el vínculo del dispositivo y la sesión del usuario en cada conexión, y mantiene un registro temporal de clientes conectados.
- `MauiApp1.Coordinator/Program.cs`: registra el hub y un publicador que consulta el diario central cada 400 ms. Solo anuncia operaciones con estado confirmado y revalida la autorización antes de enviar cada aviso; un fallo temporal de envío deja el aviso pendiente para reintento.
- `MauiApp1.Application/Sync/ICoordinatorRealtimeClient.cs` y `MauiApp1.Infrastructure/Lan/CoordinatorRealtimeClient.cs`: implementan el cliente SignalR con transporte HTTPS Long Polling y el mismo pin SHA-256 del certificado usado por la vinculación. Los avisos solo indican qué cambió; el cliente usa la consulta durable por cursor para descargar y aplicar los datos.
- `MauiApp1.Infrastructure/Sync/LocalDataChangeNotifier.cs`, `LocalCoordinatorDatabaseChangeMonitor.cs` y `LocalOperationSyncService.cs`: notifica a la interfaz luego de aplicar cambios recibidos. En Windows observa cambios externos de la base central, que ya comparte con el coordinador.
- `MauiApp1/Presentation/Views/CoordinatorRefreshPage.cs` y las vistas de Inicio, categorías, productos, existencias, compras, ventas, clientes, pagos, reportes, movimientos y conflictos: refrescan la pantalla visible tras recibir los datos, evitando reiniciar formularios de pago, edición o venta pendientes.
- `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs`, `MainPage.xaml`, `MainPage.xaml.cs`, `App.xaml.cs` y `MauiProgram.cs`: muestran conexión en vivo, inician y detienen el cliente con la sesión y el estado de primer plano, y conservan «Sincronizar ahora» como respaldo.
- `MauiApp1.Infrastructure/MauiApp1.Infrastructure.csproj` y `CoordinatorPairingClient.cs`: agregan el cliente oficial SignalR 10.0.12 y reutilizan la validación de certificado fijado para sus conexiones.
- `MauiApp1.Coordinator/Deployment/SINCRONIZACION_API.md` y `D:\Trabajo\Tesis\TASK.md`: documentan el flujo y separan la verificación manual del objetivo de latencia.
- Verificación: `MauiApp1.Coordinator` compiló Debug; MAUI Windows y Android compilaron Debug, cada uno con 0 advertencias y 0 errores. Se generó la publicación autónoma Release `MauiApp1.Coordinator/bin/publish/win-x64` para el actualizador.
- Pendiente: ejecutar el actualizador del coordinador como Administrador, instalar la app actualizada en ambos equipos y medir que las pantallas abiertas se actualicen en menos de 2 segundos. La prueba sin LAN de T7.2 queda pendiente como solicitó el usuario; no se inició T7.4.
