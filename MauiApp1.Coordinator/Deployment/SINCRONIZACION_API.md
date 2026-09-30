# API inicial de operaciones LAN

Esta API pertenece al coordinador de Windows y solo funciona por HTTPS. El dispositivo debe estar previamente vinculado. Las rutas de negocio requieren tanto `X-Device-Id: <guid>` y `Authorization: Bearer <clave-del-dispositivo>` como `X-User-Session: <sesión-de-usuario>`. El coordinador compara las credenciales de dispositivo y sesión contra hashes, liga la sesión al dispositivo y confirma que la cuenta central siga habilitada. No se acepta el identificador del dispositivo como prueba de autorización.

**Despliegue local:** al 2026-09-30 el paquete con sesiones está compilado pero aún no actualizado en el servicio Windows de este equipo; allí sigue ejecutándose el binario anterior. Ejecuta `Deployment/update-coordinator.ps1` como Administrador y confirma su verificación `/health` antes de consumir `POST /api/auth/session` o las rutas protegidas por sesión.

## Iniciar sesión compartida

`POST /api/auth/session`

Requiere HTTPS y la autorización del dispositivo en los encabezados `X-Device-Id` y `Authorization`. El cuerpo contiene `userName` y `password`; credenciales inválidas, usuario deshabilitado o dispositivo no autorizado reciben `401` sin distinguir cuál dato falló. La solicitud queda limitada por la política de intentos por IP.

Al iniciar sesión, el coordinador valida la cuenta contra la base central importada, devuelve `sessionToken`, `userId`, `userName`, `role` y `expiresAtUtc`, y guarda solo el hash SHA-256 del token. La sesión dura 12 horas, se liga al `DeviceId` y deja de autorizar si expira, la cuenta se deshabilita o el dispositivo deja de estar vinculado. El cliente debe guardar el token en el almacenamiento seguro del dispositivo y no registrarlo en logs.

## Guardar una operación

`POST /api/sync/operations`

El cuerpo JSON tiene `operationId`, `deviceId`, `actorUserId`, `operationType`, `payloadJson` y `createdAtUtc`. El tamaño total de la solicitud está limitado aproximadamente a 1.1 MB; el contenido `payloadJson` debe ser un objeto JSON y no exceder 1 MB. El tipo admite hasta 120 caracteres. Los identificadores deben ser GUID válidos y la fecha debe venir en UTC. `actorUserId` queda por compatibilidad del contrato local, pero no es una autoridad: el coordinador registra el usuario obtenido de la sesión validada.

El coordinador valida que el dispositivo del cuerpo sea el mismo que ya autenticó, toma el usuario de la sesión central y guarda el sobre completo en `CoordinatorOperations` dentro de una transacción SQLite. El `OperationId` es único: repetir el mismo identificador, dispositivo, usuario autenticado, tipo y contenido devuelve la secuencia original como aceptada; reutilizarlo con contenido/dispositivo/usuario diferente devuelve `409 Conflict`. La respuesta `200` confirma que el sobre quedó guardado de forma durable en el coordinador.

La tabla es un diario de operaciones recibidas para que T7.2 implemente su aplicación idempotente a las tablas de negocio. Recibir una operación no implica todavía que ya haya cambiado el inventario central ni que otros dispositivos hayan aplicado el cambio.

## Consultar operaciones de otros dispositivos

`GET /api/sync/changes?afterSequence=<n>&limit=<n>`

Requiere la autenticación de dispositivo y la sesión de usuario. Devuelve las operaciones posteriores al cursor, en orden ascendente, excepto las originadas por el mismo dispositivo. `limit` se limita al rango 1–500 y usa 100 cuando se omite. `nextSequence` es la secuencia del último elemento devuelto (o conserva el cursor si no hubo resultados). El cliente debe persistir ese cursor solo después de aplicar localmente los elementos; la aplicación local y la recuperación tras reconexión se implementan en T7.2.

## Límites actuales

- La API valida sesiones compartidas de usuarios existentes en la base Windows central y atribuye cada sobre al usuario de la sesión. Todavía no hay flujo MAUI para iniciar/renovar sesión central ni sincronizar altas de usuarios hechas sin conexión; la integración completa y conciliación de cuentas locales se hace con el cliente/outbox de T7.2.
- Esta API aún almacena operaciones en el diario; no aplica esas operaciones a las tablas de negocio. Por ello aún no mantiene un catálogo/inventario unificado en todos los dispositivos hasta completar T7.2.
- Aún no se validan los tipos de operación contra reglas de dominio, ni se aplican a productos, existencias, ventas, compras, clientes, cuentas o pagos. Corresponde a T7.2.
- Aún no se publica SignalR; corresponde a T7.3.
- Los endpoints no sustituyen la cola Outbox local: un cliente solo debe marcar una operación confirmada tras recibir respuesta de aceptación durable.
