# API inicial de operaciones LAN

Esta API pertenece al coordinador de Windows y solo funciona por HTTPS. El dispositivo debe estar previamente vinculado. Cada solicitud protegida incluye `X-Device-Id: <guid>` y `Authorization: Bearer <clave-del-dispositivo>`; el coordinador compara el hash de la clave en tiempo constante. No se acepta el identificador del dispositivo como prueba de autorización.

## Guardar una operación

`POST /api/sync/operations`

El cuerpo JSON tiene `operationId`, `deviceId`, `actorUserId`, `operationType`, `payloadJson` y `createdAtUtc`. El tamaño total de la solicitud está limitado aproximadamente a 1.1 MB; el contenido `payloadJson` debe ser un objeto JSON y no exceder 1 MB. El tipo admite hasta 120 caracteres. Los identificadores deben ser GUID válidos y la fecha debe venir en UTC.

El coordinador valida que el dispositivo del cuerpo sea el mismo que ya autenticó, y guarda el sobre completo en `CoordinatorOperations` dentro de una transacción SQLite. El `OperationId` es único: repetir el mismo identificador y contenido devuelve la secuencia original como aceptada; reutilizarlo con contenido/dispositivo/usuario diferente devuelve `409 Conflict`. La respuesta `200` confirma que el sobre quedó guardado de forma durable en el coordinador.

La tabla es un diario de operaciones recibidas para que T7.2 implemente su aplicación idempotente a las tablas de negocio. Recibir una operación no implica todavía que ya haya cambiado el inventario central ni que otros dispositivos hayan aplicado el cambio.

## Consultar operaciones de otros dispositivos

`GET /api/sync/changes?afterSequence=<n>&limit=<n>`

Usa la misma autenticación de dispositivo. Devuelve las operaciones posteriores al cursor, en orden ascendente, excepto las originadas por el mismo dispositivo. `limit` se limita al rango 1–500 y usa 100 cuando se omite. `nextSequence` es la secuencia del último elemento devuelto (o conserva el cursor si no hubo resultados). El cliente debe persistir ese cursor solo después de aplicar localmente los elementos; la aplicación local y la recuperación tras reconexión se implementan en T7.2.

## Límites actuales

- No existe aún autenticación compartida de sesiones de usuario ni validación de `actorUserId`; permanece en el sobre para conservar la atribución local y su validación central se hará en el subpaso correspondiente de T7.1.
- Aún no se validan los tipos de operación contra reglas de dominio, ni se aplican a productos, existencias, ventas, compras, clientes, cuentas o pagos. Corresponde a T7.2.
- Aún no se publica SignalR; corresponde a T7.3.
- Los endpoints no sustituyen la cola Outbox local: un cliente solo debe marcar una operación confirmada tras recibir respuesta de aceptación durable.
