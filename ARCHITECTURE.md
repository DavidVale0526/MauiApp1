# Arquitectura inicial

La solución mantiene una sola aplicación .NET MAUI para Windows y Android. Las reglas compartidas se escribirán una vez; el código específico de plataforma se limitará a integraciones que MAUI no pueda resolver de forma común.

## Proyectos y dependencias

- `MauiApp1`: presentación MAUI, vistas, ViewModels y punto de composición de dependencias.
- `MauiApp1.Application`: casos de uso y contratos de aplicación; referencia el dominio.
- `MauiApp1.Domain`: reglas y tipos del negocio sin dependencia de interfaz, base de datos o plataforma.
- `MauiApp1.Infrastructure`: implementaciones de persistencia y comunicación; implementará contratos de Application y podrá usar Domain.

La dirección de dependencias es `Infrastructure -> Application -> Domain`. La aplicación MAUI referencia Application e Infrastructure para conectar implementaciones al iniciar. Los modelos, servicios y esquemas se incorporan por módulos; la comunicación LAN corresponde a la Fase 7 y aún no forma parte del catálogo local.

## Compilación y ejecución

Desde la carpeta `MauiApp1`:

- Windows: `dotnet build .\MauiApp1\MauiApp1.csproj -f net10.0-windows10.0.19041.0`
- Android: `dotnet build .\MauiApp1\MauiApp1.csproj -f net10.0-android`
- Para ejecutar, seleccionar el destino Windows o un emulador/dispositivo Android en Visual Studio y usar **Iniciar**. La aplicación es nativa instalada; no abre navegador.

Android requiere la carga MAUI de Android y un SDK/emulador o dispositivo configurado. Windows requiere la carga MAUI de Windows y las herramientas de escritorio de Visual Studio.

## Persistencia local (T1.2)

- Cada instalación guarda `miscelanea-ari.db` dentro del directorio privado de datos de la aplicación. Inicializa o actualiza su esquema con migraciones EF Core al arrancar; la creación no consulta la LAN ni Internet.
- `MauiApp1.Infrastructure` contiene el `LocalDbContext` y las migraciones. Para crear una migración desde la carpeta `MauiApp1`: `dotnet tool restore`, luego `dotnet tool run dotnet-ef migrations add <Nombre> --project .\MauiApp1.Infrastructure\MauiApp1.Infrastructure.csproj --startup-project .\MauiApp1.Infrastructure\MauiApp1.Infrastructure.csproj --framework net10.0 --output-dir Persistence\Migrations`.
- La migración inicial crea `DeviceIdentity` (un identificador Guid persistido por instalación) y `OutboxMessages` (operación Guid, dispositivo, usuario responsable, tipo, JSON, fecha UTC, estado, intentos y acuse UTC). El identificador de operación es también la clave de idempotencia. Las filas confirmadas se conservan; solo se excluyen de la cola pendiente después del acuse del coordinador.
- `IOperationOutbox.EnqueueAsync` agrega el registro al `LocalDbContext` compartido de su ámbito sin confirmar. La operación de negocio debe agregar sus cambios en ese mismo ámbito y llamar una sola vez a `ILocalUnitOfWork.SaveChangesAsync`; el proveedor relacional guarda todos esos cambios en una transacción. Los envíos fallidos siguen como pendientes y pueden reintentarse.
- Los identificadores de producto y categoría son Guid (`ProductId` y `CategoryId`). Las fechas se manejan como `DateTime` UTC y se guardan en SQLite como ticks enteros, para preservar el instante UTC y ordenar con precisión. Los importes se representan en unidades menores enteras con dos decimales; las cantidades se representan en milésimas enteras con tres decimales. Cada producto conserva su propia unidad editable. La moneda aprobada para el sistema es el córdoba nicaragüense, mostrado como `C$`.
- Se usa EF Core 10.0.12 y `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5. La versión explícita del bundle evita restaurar la versión 2.1.11 señalada por la auditoría de NuGet.

La base central no se comparte como archivo con las aplicaciones. Se agregará al servicio coordinador Windows cuando se implemente ese módulo; en esta etapa cada app solo accede a su archivo privado local.

El inventario de cambios por fase se conserva en `HISTORIAL_CAMBIOS/`.

## Acceso local (T1.3)

- En la primera apertura, cada instalación solicita crear una sola cuenta Administrador. Un Administrador con sesión iniciada puede agregar cuentas Cajero en ese mismo dispositivo. Windows y Android mantienen sus registros de cuentas separados hasta que el coordinador LAN y la autenticación compartida se implementen en T7.1.
- Cada cuenta usa nombre de usuario normalizado para no distinguir mayúsculas/minúsculas. El secreto no se persiste: se guarda una sal aleatoria y una derivación PBKDF2-HMAC-SHA256 de 600,000 iteraciones. Las contraseñas deben tener al menos 15 caracteres y pueden contener espacios y cualquier carácter; no hay reglas de composición.
- La sesión vive solo en memoria, se borra al cerrar sesión y no se restaura al reiniciar la app. La Shell muestra la vista principal solo durante una sesión autenticada. El rol Cajero todavía no tiene restricciones sobre funciones de negocio; la matriz queda pendiente.
- No hay recuperación de contraseña ni restablecimiento desde la app. Un olvido requiere una futura decisión/proceso de soporte; no se guarda una contraseña de recuperación.

## Categorías (T2.1)

- `Categories` guarda identificador Guid, nombre visible y nombre normalizado con índice único. Se quitan espacios en extremos y la comparación no distingue mayúsculas/minúsculas.
- Crear, renombrar o eliminar una categoría guarda también una operación pendiente en la misma transacción local. No requiere LAN ni Internet.
- Se permite renombrar una categoría asignada; se bloquea eliminarla mientras tenga productos asociados mediante clave foránea restrictiva entre `Products` y `Categories`. La UI muestra un error recuperable si SQLite rechaza un borrado por esa relación.
- La pantalla MAUI de categorías solo aparece después de iniciar sesión y es compartida por Windows y Android. No se añaden filtros, campos adicionales ni permisos por rol en este paso.

## Productos (T2.2)

- `Products` conserva identificador Guid, nombre, precio de venta en unidades menores (2 decimales), existencia y mínimo en milésimas (3 decimales), unidad editable y `CategoryId` con FK `RESTRICT`.
- Nombre y unidad son obligatorios y se recortan al guardar. Se acepta precio y mínimo igual a cero; no se permiten valores negativos. El nombre no se fuerza único y no se añade SKU/código.
- El alta inicia `StockMilliUnits` en cero. La edición del catálogo no puede cambiar existencia; los movimientos de T3 serán su única vía de actualización.
- Retirar un producto lo marca inactivo en vez de borrarlo. El registro y su identificador permanecen disponibles para historial; se puede reactivar. Alta, edición y cambio de estado escriben el snapshot completo en Outbox en la misma transacción local.
- La página MAUI compartida permite alta/consulta/edición/desactivación/reactivación y muestra precio, categoría, unidad, existencia y mínimo. La existencia de nuevos productos no requiere LAN ni Internet.

## Consulta de existencias (T2.3)

- La pantalla de solo lectura reutiliza `IProductService.GetAllAsync`; no crea otra tabla ni copia el catálogo. Muestra todos los productos y distingue activos/inactivos, con categoría, precio vigente, existencia/unidad y mínimo.
- Se recarga al abrir y permite volver a consultar con gesto de actualización. No tiene búsqueda, filtros ni alertas porque no fueron aprobados en este módulo. La sincronización LAN queda en T7; sus cambios deberán actualizar la misma tabla local que esta consulta lee.

## Movimientos de inventario (T3.1)

- `InventoryMovements` es el historial de líneas de movimiento. Cada línea conserva identificador, `OperationId`, número de línea, producto, snapshot histórico de nombre/unidad, tipo, cantidad en milésimas, motivo opcional, fecha UTC y usuario responsable. La clave única `(OperationId, LineNumber)` y la clave de idempotencia del Outbox impiden volver a aplicar el mismo movimiento. La migración T3.5 completa snapshots anteriores desde el catálogo disponible al migrar.
- Los tipos de dominio son `PurchaseEntry`, `SaleExit` y `OtherExit`. Una salida distinta de venta exige motivo. Una operación agrupa líneas de un mismo tipo; futuras entidades de compra y venta deberán reutilizar su `OperationId` como vínculo.
- `Products.StockMilliUnits` conserva el saldo actual consultable. `EfInventoryMovementService` actualiza saldo, añade historial y añade Outbox usando el mismo contexto/`SaveChanges`, que EF Core confirma como una transacción SQLite. El servicio solo agrega movimientos; no ofrece editar ni borrar el historial.
- El reintento idéntico del mismo usuario y `OperationId` devuelve “ya aplicado”; reutilizar el identificador con otro usuario/tipo/contenido se rechaza. La cantidad debe ser positiva y las fechas deben llegar en UTC.
- Se rechazan salidas locales que dejarían saldo negativo, según la decisión T3.4; T4.1 debe aplicar la misma regla.
- `EfInventoryMovementService.GetHistoryAsync` ofrece una lectura autenticada, local, de solo lectura, ordenada por fecha descendente. La pantalla muestra tipo, snapshot de producto/cantidad/unidad, fecha local, motivo y referencia corta del `OperationId`. Compras y futuras ventas mantienen el vínculo por `OperationId`.
- El payload nuevo de movimientos incluye nombre/unidad históricos para replicar el mismo significado en LAN. Los payloads anteriores pueden carecer de esos campos; T7.2 debe conservar compatibilidad al recibirlos. La consulta no agrega filtros ni una tabla paralela y queda disponible como fuente para reportes.

## Compras y entradas (T3.2)

- `Purchases` guarda una cabecera identificada por el mismo `OperationId` de la compra, con fecha/hora UTC y usuario; `PurchaseLines` guarda producto, número de línea, cantidad en milésimas y copias históricas del nombre/unidad. La combinación `(OperationId, LineNumber)` es única y las relaciones de producto/usuario son restrictivas para conservar historial. La migración completa snapshots vacíos preexistentes desde el catálogo vigente.
- El alcance aprobado de compra contiene solo fecha/hora automática, productos y cantidades recibidas. No incluye proveedor, costo, documento, lotes ni vencimiento. La pantalla incluye solo productos activos y acumula cantidades repetidas del mismo producto.
- `EfPurchaseService` usa el escritor común de movimientos para preparar en el mismo contexto EF la compra, sus líneas, los movimientos `PurchaseEntry`, el saldo del producto y Outbox. Una única llamada a `SaveChangesAsync` persiste todo en la transacción local SQLite.
- El payload Outbox `Purchase.Recorded` contiene la compra completa, incluidos snapshots de nombre/unidad en nuevas operaciones. Repetir la misma operación y contenido no vuelve a sumar existencias; reutilizar el `OperationId` con otro contenido se rechaza. Fallos de validación/persistencia no registran una parte de la compra.
- La página presenta el resumen antes de guardar. Una respuesta fallida conserva el borrador de operación para reintentar sin cambiar su identificador/contenido.
- T3.3 consulta `Purchases` y `PurchaseLines`, sin copiar datos ni aplicar filtros. Ordena por fecha UTC descendente, muestra la hora en la zona local del dispositivo y presenta snapshots de producto/cantidad/unidad. El usuario puede recargar manualmente.
- Compatibilidad pendiente para T7.2: payloads `Purchase.Recorded` generados antes de los snapshots no contienen nombre/unidad; el coordinador deberá admitirlos y resolver esos datos sin cambiar el `OperationId`.

## Salidas de inventario no comerciales (T3.4)

- La pantalla permite seleccionar un producto activo, una cantidad positiva en milésimas y una causa: `Vencimiento`, `Daño`, `Pérdida` u `Otro`. `Otro` requiere descripción; el texto completo queda en el campo `Reason` del movimiento y en el payload Outbox.
- Cada confirmación crea un `OperationId` estable y una salida `OtherExit`; la repetición exacta se reconoce como ya aplicada y no descuenta existencias otra vez. Un mismo identificador con motivo/cantidad/contenido distinto se rechaza.
- Política aprobada: bloquear si el saldo local resultante sería negativo. La pantalla valida el saldo actual para anticipar el error y el escritor lo vuelve a validar al guardar. La respuesta incluye saldo disponible y cantidad solicitada.
- Producto, saldo, línea inmutable de movimiento y Outbox se guardan en la misma transacción SQLite. Solo se aceptan productos activos; la consulta/registro local no requiere LAN ni Internet. T4.1 debe respetar la misma política de stock negativo.
- La pantalla es de una salida y un producto por operación, con resumen/confirmación. Las anulaciones/correcciones y los conflictos por operaciones simultáneas fuera de LAN siguen pendientes.

## Registro de ventas (T4.1)

- `Sales` guarda una cabecera por `OperationId`, fecha UTC, usuario y total en centavos. `SaleLines` conserva producto, nombre/unidad/precio históricos, cantidad en milésimas e importe de línea en centavos. Renglones con el mismo producto se consolidan; el mismo producto debe conservar un único snapshot de nombre, unidad y precio por venta.
- El cálculo convierte precio a centavos y cantidad a milésimas. Cada subtotal de línea se redondea a centavos con `MidpointRounding.AwayFromZero` (mitades hacia arriba); el total de venta suma los importes de línea ya redondeados.
- `EfSaleService` aplica el stock policy `Reject` e incorpora `SaleExit`, saldo actualizado, venta/cabecera/renglones y payload `Sale.Recorded` en el mismo contexto y `SaveChanges`. SQLite confirma la operación local de forma atómica. La falta de existencia, el producto inactivo o la operación inválida no alteran saldos.
- Un `OperationId` ya procesado con el mismo actor, tipo y payload devuelve éxito idempotente; reutilizarlo con contenido distinto se rechaza. Los nombres/unidades snapshots del renglón también se guardan en el movimiento para que T3.5 muestre la misma historia.
- La pantalla permite agregar y quitar líneas antes de confirmar, enseña precio y stock disponible, muestra importes por línea y total, y solicita confirmación. Reintentos mantienen el mismo borrador/id de operación. No registra forma de pago, cliente/crédito, impuesto, descuento o comprobante.

## Historial de ventas (T4.2)

- `ISaleService.GetHistoryAsync` retorna ventas locales solo para una sesión autenticada, ordenadas por fecha UTC descendente y con sus renglones persistidos. No agrega una copia del catálogo ni actualiza saldos.
- Cada registro muestra fecha/hora local, usuario, referencia corta al `OperationId`, nombre/unidad/precio/cantidad/importe históricos por renglón y total de la venta.
- La pantalla no permite editar/eliminar una venta. Anulaciones, correcciones, filtros, cobros y comprobantes siguen fuera de alcance hasta aprobar sus reglas.

## Ticket interno de venta (T4.3)

- Se presenta después de una venta confirmada y se puede volver a abrir desde el historial. Usa exclusivamente la venta y los renglones persistidos, conservando sus snapshots.
- Muestra referencia corta del `OperationId`, fecha/hora local, usuario, productos/cantidades/precios/importes y total con símbolo `C$` (córdobas nicaragüenses).
- Es solo para consulta en pantalla y se identifica como no fiscal. No imprime ni agrega cobros, impuestos o campos fiscales; una factura fiscal requiere definición y aprobación aparte.
