# API inicial de operaciones LAN

Esta API pertenece al coordinador de Windows y solo funciona por HTTPS. El dispositivo debe estar previamente vinculado. Las rutas de negocio requieren tanto `X-Device-Id: <guid>` y `Authorization: Bearer <clave-del-dispositivo>` como `X-User-Session: <sesión-de-usuario>`. El coordinador compara las credenciales de dispositivo y sesión contra hashes, liga la sesión al dispositivo y confirma que la cuenta central siga habilitada. No se acepta el identificador del dispositivo como prueba de autorización.

**Despliegue local:** el servicio Windows se actualizó correctamente el 2026-09-30; `/health` respondió `ok`. Las rutas protegidas por sesión ya están desplegadas en el equipo coordinador.

### Escrituras SQLite desde la aplicación Windows

La interfaz MAUI de Windows consulta y registra cambios directamente en la base central del coordinador. Al instalar o actualizar el servicio, los scripts conceden permiso `Modify` únicamente a la cuenta de Windows indicada por `-WindowsAppUser` (por defecto, la cuenta que ejecuta PowerShell). El permiso cubre la carpeta de datos y los archivos SQLite `-wal` y `-shm`; los archivos auxiliares necesitan escritura aunque la base principal permita lectura. Si la aplicación se ejecuta con otra cuenta de Windows, pasa esa cuenta explícitamente al instalador o actualizador. No se otorgan permisos de escritura a todos los usuarios del equipo.

## Iniciar sesión compartida

`POST /api/auth/session`

Requiere HTTPS y la autorización del dispositivo en los encabezados `X-Device-Id` y `Authorization`. El cuerpo contiene `userName` y `password`; credenciales inválidas, usuario deshabilitado o dispositivo no autorizado reciben `401` sin distinguir cuál dato falló. La solicitud queda limitada por la política de intentos por IP.

Al iniciar sesión, el coordinador valida la cuenta contra la base central importada, devuelve `sessionToken`, `userId`, `userName`, `role` y `expiresAtUtc`, y guarda solo el hash SHA-256 del token. La sesión dura 12 horas, se liga al `DeviceId` y deja de autorizar si expira, la cuenta se deshabilita o el dispositivo deja de estar vinculado. El cliente debe guardar el token en el almacenamiento seguro del dispositivo y no registrarlo en logs.

## Guardar una operación

`POST /api/sync/operations`

El cuerpo JSON tiene `operationId`, `deviceId`, `actorUserId`, `operationType`, `payloadJson` y `createdAtUtc`. El tamaño total de la solicitud está limitado aproximadamente a 1.1 MB; el contenido `payloadJson` debe ser un objeto JSON y no exceder 1 MB. El tipo admite hasta 120 caracteres. Los identificadores deben ser GUID válidos y la fecha debe venir en UTC. `actorUserId` queda por compatibilidad del contrato local, pero no es una autoridad: el coordinador registra el usuario obtenido de la sesión validada.

El coordinador valida que el dispositivo del cuerpo sea el mismo que ya autenticó, toma el usuario de la sesión central y guarda el sobre completo en `CoordinatorOperations` dentro de una transacción SQLite. En esa misma transacción aplica el cambio de negocio central cuando el tipo y sus datos son válidos. El `OperationId` es único: repetir el mismo identificador, dispositivo, usuario autenticado, tipo y contenido devuelve la secuencia original; reutilizarlo con contenido/dispositivo/usuario diferente devuelve `409 Conflict`. Los conflictos de negocio se conservan para revisión y no cambian silenciosamente los datos centrales.

La tabla es el diario durable y registro de cambios central. Al aceptar una operación remota, el coordinador persiste el sobre y la modificación de negocio de forma atómica. La instalación MAUI de Windows ya trabaja sobre la base central; sus operaciones Outbox se publican al diario como cambios ya aplicados, sin ejecutar el cambio de negocio por segunda vez.

## Consultar operaciones de otros dispositivos

`GET /api/sync/changes?afterSequence=<n>&limit=<n>`

Requiere la autenticación de dispositivo y la sesión de usuario. Devuelve los cambios posteriores al cursor que fueron aplicados centralmente, en orden ascendente, excepto los originados por el mismo dispositivo. `limit` se limita al rango 1–500 y usa 100 cuando se omite. `nextSequence` es la secuencia del último elemento devuelto (o conserva el cursor si no hubo resultados). El cliente aplica cada cambio localmente antes de guardar el cursor. Si una divergencia requiere una decisión, detiene el cursor y presenta el caso para revisión del Administrador.

## Límites actuales

- La API valida sesiones compartidas de usuarios existentes en la base Windows central y atribuye cada sobre al usuario de la sesión. Las cuentas Cajero nuevas se crean en el coordinador solo cuando hay LAN; no se crean cuentas compartidas sin conexión.
- La aplicación MAUI procesa la cola local cuando la app vuelve a Inicio o cambia la conectividad, conserva operaciones sin acuse, y descarga cambios desde un cursor durable. El estado LAN, cola, conflictos y última sincronización se presenta en Inicio.
- El Administrador puede revisar conflictos de producto/categoría y elegir la versión central o la propuesta recibida; la resolución queda registrada como un nuevo evento durable para converger los demás dispositivos.
- Los cambios de existencia conservan la regla aprobada: las ventas se registran aunque el stock central no alcance, sin saldo negativo, y el faltante queda marcado para ajuste posterior del Administrador tras conteo físico.
- Aún no se publica SignalR; corresponde a T7.3.
- Los endpoints no sustituyen la cola Outbox local: un cliente solo debe marcar una operación confirmada tras recibir respuesta de aceptación durable.
