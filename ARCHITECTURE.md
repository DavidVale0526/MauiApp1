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

La base central no se comparte como archivo con las aplicaciones. El proyecto coordinador Windows la inicializa en `ProgramData`; cada aplicación MAUI conserva su archivo privado local. El intercambio de cambios entre esos archivos se implementará en T7.2.

El inventario de cambios por fase se conserva en `HISTORIAL_CAMBIOS/`.

## Acceso local (T1.3)

- En la primera apertura, cada instalación solicita crear una sola cuenta Administrador. Un Administrador con sesión iniciada puede agregar cuentas Cajero en ese mismo dispositivo. En un dispositivo vinculado, el inicio consulta al coordinador; cuando no responde se permite el acceso local. La asociación de cuentas central-local está implementada en T7.2, pero la sincronización de operaciones de negocio sigue pendiente.
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
- Se recarga al abrir y permite volver a consultar con gesto de actualización. No tiene búsqueda ni filtros. Las alertas se añadieron posteriormente en T6.3. La sincronización LAN queda en T7; sus cambios deberán actualizar la misma tabla local que esta consulta lee.

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

## Clientes y ventas a crédito (T5.1)

- `Customers` conserva identificador Guid, nombre visible y normalizado, teléfono opcional y fecha UTC de alta. El nombre se recorta y normaliza sin distinguir mayúsculas/minúsculas; un índice único impide duplicados. El alta local se guarda con `Customer.Created` en Outbox.
- La pantalla «Clientes y crédito» registra y consulta clientes y suma los saldos pendientes locales. No edita ni elimina clientes; no se incorporan otros datos personales.
- La venta conserva `IsCredit` y `CustomerId`. Para crédito requiere cliente y vencimiento a 15 días de la fecha de venta UTC. `Receivables`, con `OperationId` como vínculo único de venta, guarda cliente, monto original, saldo inicial y vencimiento.
- El registro de venta a crédito persiste venta, renglones históricos, cuenta por cobrar, movimiento de salida, saldo de existencias y Outbox `Sale.Recorded` bajo la misma unidad de trabajo SQLite. La idempotencia abarca importe, cliente y datos de crédito para que un reintento no duplique deuda ni salida.
- El historial y ticket distinguen contado/crédito y muestran cliente y vencimiento si corresponde.
- `Payments` conserva cada abono como registro inmutable con identificador Guid, deuda, actor, monto en unidades menores y fecha/hora UTC. Cada pago se agrega al Outbox con el mismo identificador de idempotencia.
- `EfPaymentService` reduce `Receivables.OutstandingMinorUnits`, agrega el pago y Outbox en un solo `SaveChangesAsync`. La concurrencia optimista sobre el saldo detecta cambios simultáneos; sobrepago o conflicto no dejan escrituras parciales.
- Se aceptan varios abonos siempre que sean positivos y no superen el saldo pendiente. Un reintento con el mismo identificador/contenido no vuelve a reducirlo; usar ese identificador con otros datos se rechaza.
- La pantalla «Crédito y pagos» consulta cuentas (incluidas las saldadas) y el historial de pagos asociado a la deuda seleccionada. Muestra hora local, usuario, referencia y montos en córdobas (`C$`). No guarda medio de pago ni permite editar/eliminar registros.

## Especificación de reportes (T6.1)

- **Ventas:** lista por rango inclusivo de fechas locales con fecha/hora, referencia corta, usuario, modalidad contado/crédito, cliente si aplica y total de cada venta en `C$`. Filtros por modalidad y cliente. El total es venta registrada; no equivale a dinero cobrado.
- **Inventario:** fotografía actual con producto, categoría, estado activo/inactivo, existencia y unidad, mínimo y unidad. Filtros por categoría y estado. No se calcula valoración porque no hay costos de compra persistidos.
- **Movimientos:** lista por rango inclusivo de fechas locales con fecha/hora, nombre/unidad históricos, tipo, cantidad, motivo, referencia corta y usuario. Filtros por producto y tipo; no suma magnitudes de tipos distintos.
- Los tres reportes muestran el aviso «Datos locales de este dispositivo; pueden incluir operaciones aún no sincronizadas». No se agregan ganancia/margen, costos, rankings, cobros, gráficos ni exportación.
- T6.3 define la alerta para productos activos cuando existencia sea menor o igual al mínimo; su presentación está en Inicio y Consulta de existencias.

## Reportes locales (T6.2)

- `IReportService` ofrece consultas autenticadas y no modifica las tablas de negocio. `EfReportService` proyecta desde ventas/usuarios/clientes, productos/categorías y movimientos/usuarios usando consultas SQLite `AsNoTracking`.
- Los filtros temporales reciben límites UTC: la interfaz transforma las fechas calendario locales inclusivas a inicio UTC inclusivo y comienzo del día siguiente exclusivo. Fechas visibles se presentan en la zona local del dispositivo.
- La pantalla «Reportes» permite cambiar entre ventas, inventario y movimientos, ajustar los filtros acordados y recargar. Muestra aviso de datos locales, periodos, estado sin resultados y la hora de la fotografía de inventario.
- Ventas y movimientos aparecen en orden cronológico descendente; el inventario por categoría y nombre. No se muestran cifras agregadas no acordadas.

## Alertas de existencia mínima (T6.3)

- `LowStockProductQuery` comparte un criterio único: solo productos activos con existencia menor o igual al mínimo, ordenados alfabéticamente. La comparación usa las cantidades enteras en milésimas; no depende del formato decimal mostrado.
- Inicio muestra el resumen de productos bajo mínimo cada vez que aparece, por lo que al volver de registrar una compra, venta o salida se consulta el inventario local actualizado.
- Consulta de existencias muestra el mismo resumen y lo recalcula al abrir y al ejecutar el gesto de actualización. El panel incluye nombre, existencia/unidad y mínimo/unidad; si no hay alertas presenta un estado vacío informativo.
- No hay notificaciones en segundo plano ni otro almacenamiento para alertas. La pantalla refleja los datos locales de la instalación hasta que T7 implemente sincronización.

## Coordinador LAN (T7.1 en curso)

- `MauiApp1.Coordinator` es un proyecto separado basado en ASP.NET Core sobre .NET 10; referencia Application e Infrastructure y registra el host SignalR. `UseWindowsService` configura su ciclo de vida de Windows Service. En esta PC está instalado en `C:\Program Files\Miscelanea Ari\Coordinator` como `MiscelaneaAriCoordinator`, cuenta LocalSystem e inicio Automatic.
- El URL de escucha se configura con `Coordinator:Url` en `appsettings.json` o la variable `Coordinator__Url`; el servicio de producción escucha por HTTPS en `https://0.0.0.0:5080` y exige un certificado de máquina configurado por huella. La regla de firewall permite TCP 5080 solo para el ejecutable instalado y el perfil Private. La Wi-Fi activa de esta PC (`CLARO_hU5Us7`) está clasificada actualmente como Public, así que la regla bloquea acceso entrante por ella hasta que el usuario confirme y cambie esa red confiable a Private. El perfil de Visual Studio escucha solo en `http://127.0.0.1:5080` y usa una base de desarrollo separada bajo `bin`.
- Los errores de configuración y del arranque inicial se capturan y se escriben como mensaje legible con código de salida 1; no se lanzan excepciones sin controlar para iniciar el depurador JIT.
- La vinculación usa el `DeviceId` persistente de la instalación. El Administrador se reautentica contra la base central para generar un código de ocho dígitos, válido cinco minutos; generar otro invalida el anterior. Antes de canjearlo, el cliente genera una clave aleatoria de 256 bits y la guarda como vínculo pendiente en SecureStorage. El coordinador guarda su hash y consume el código en la misma transacción. Un reintento con el mismo dispositivo y clave recupera la confirmación; otro dispositivo o clave no reutiliza esa autorización. Los códigos se almacenan con sal y hash. El canje exige HTTPS y limita intentos por IP.
- `DevicePairingPage` está disponible después del acceso local en ambas plataformas. Windows muestra el formulario de generación solo para Administrador; ambos destinos permiten canjear códigos. Application define contratos, Infrastructure implementa HTTPS y MAUI aporta SecureStorage. La clave y la configuración se guardan juntas bajo `coordinator-device-link-v1`; la contraseña del Administrador se limpia al terminar cada intento y no se persiste.
- El cliente exige IP privada IPv4 o loopback y HTTPS, fija la huella SHA-256 del certificado, verifica sus fechas y desactiva redirecciones y proxies. Windows presenta un QR temporal que lleva dirección, huella y código de un solo uso; Android puede leerlo para rellenar los campos. No contiene contraseñas ni el token permanente del dispositivo. La verificación criptográfica se mantiene antes del canje. Una comprobación HTTPS de `/health` precede al guardado pendiente, para que una huella mal escrita no bloquee la configuración. Una vinculación pendiente puede corregir la IP conservando la misma huella y clave.
- `PendingDevicePairings`, `AuthorizedDevices` y las sesiones de usuarios pertenecen al coordinador. Las rutas de negocio validan la clave de dispositivo y sesión central. La pantalla permite que cada dispositivo revoque su propia autorización por HTTPS con certificado fijado; el coordinador comprueba el token del dispositivo, revoca también sus sesiones en una transacción y responde idempotentemente. Solo tras confirmar el servidor se elimina la credencial de SecureStorage. Una caída de red conserva la credencial para reintentar; los datos de negocio locales y el identificador estable del dispositivo no se borran.
- La base central está en `CommonApplicationData\\Miscelanea Ari\\Coordinator\\miscelanea-ari-central.db` (normalmente `C:\\ProgramData\\...`); se puede cambiar con `Coordinator:DataDirectory`. Antes de arrancar, el host exige la importación inicial. `--bootstrap-from <ruta-db-Windows> --data-directory <directorio>` verifica integridad SQLite, guarda un respaldo fechado en `Backups` y copia con SQLite Backup API sin modificar el archivo local ni sobrescribir una central existente. El servicio es el único proceso que accederá a la base central.
- La base Windows se importó y verificó el 2026-09-30; los respaldos están documentados en `Respaldos/IMPORTACION_INICIAL.md`. La base Android se conserva intacta y no se combina automáticamente; su integración como dispositivo compartido se prepara en T7.2.
- `GET /health` solo devuelve el estado genérico del proceso. Existen las rutas de vinculación, generación de códigos y sesión compartida. `POST /api/sync/operations` y `GET /api/sync/changes` exigen clave Bearer de dispositivo y sesión de usuario central; el contrato está en `MauiApp1.Coordinator/Deployment/SINCRONIZACION_API.md`. El diario aún no modifica las tablas de negocio y no constituye sincronización completa. La aplicación de operaciones se implementa en T7.2 y SignalR en T7.3.
- Inicio consulta `GET /health` al reaparecer, únicamente si existe una vinculación guardada; la petición reutiliza HTTPS con la huella SHA-256 fijada y el timeout de diez segundos del cliente. La pantalla distingue dispositivo sin configurar, coordinador disponible, vinculación pendiente y coordinador sin respuesta. El estado es informativo y los datos locales siguen utilizables; al regresar a Inicio se vuelve a comprobar. `/health` confirma la respuesta del proceso, no la sincronización de datos de negocio.
- Inicio también presenta por separado el estado de conectividad de plataforma (`IConnectivity.NetworkAccess`) y la respuesta LAN de `/health`; los cambios de conectividad actualizan ambos mensajes. `Local` indica acceso local sin Internet detectado; aun así, la LAN se decide por la respuesta al IP HTTPS privado del coordinador y no por una comprobación de ISP.
- Revisión de rutas de red en el código: la app solo crea `HttpClient` para la dirección privada fijada del coordinador; no hay solicitudes a Internet/nube ni otros clientes HTTP externos. Persistencia, catálogo, ventas, compras, movimientos, clientes, pagos, reportes y alertas operan contra SQLite local. La consulta de la conectividad del sistema operativo es informativa y no bloquea estas operaciones.
- El coordinador ofrece `POST /api/auth/session` para validar una cuenta de la base central desde un dispositivo ya vinculado. Devuelve un token aleatorio de sesión de 12 horas, almacena solo su hash y lo liga al dispositivo; en cada solicitud de negocio confirma que la sesión no expiró y que la cuenta central sigue habilitada. Las rutas del diario requieren tanto token de dispositivo como `X-User-Session`; la identidad que se registra se deriva de esa sesión, no del `ActorUserId` enviado en el sobre.
- El servicio coordinador fue actualizado correctamente el 2026-09-30 con publicación autónoma `win-x64`; el equipo no requiere tener instalado el runtime .NET x64. El respaldo de DB/binarios de esa actualización está en `C:\ProgramData\Miscelanea Ari\Coordinator\Backups\PreUpdate-20260930-083716`. El actualizador comprueba `/health` y restaura los binarios anteriores si falla. La ruta de desvinculación se probó sin token y respondió 401 como corresponde; el usuario confirmó en Android el 2026-10-01 que el dispositivo se desvincula correctamente.
- Política aprobada para T7.4 el 2026-10-01: respaldo diario del coordinador, retención de 30 días, respaldo adicional antes de cada actualización y copia externa a memoria USB; no usar nube. La implementación y prueba de restauración quedan para T7.4.
- El formulario MAUI autentica contra `/api/auth/session` cuando el dispositivo está vinculado y el coordinador responde; las credenciales se validan por HTTPS fijado, y token/usuario central/vencimiento se guardan en SecureStorage. Si la conexión no responde, se permite el acceso local; si el coordinador responde y rechaza las credenciales, no se acepta una cuenta local alternativa. Después de autenticación central correcta se asocia la identidad central a una cuenta local conservando su `UserId` y sus referencias históricas; si falta una cuenta local, se crea su proyección local con verificador PBKDF2 para acceso offline. Los datos locales no se fusionan ni eliminan. El envío del Outbox todavía no consume esta sesión; esa integración queda pendiente dentro de T7.2.
- `UserAccounts.CoordinatorUserId` mantiene un mapeo local-central opcional y único, aplicado mediante la migración `CoordinatorUserMapping`; los historiales continúan referenciando su identificador local. La contraseña legible no se persiste: se genera un verificador PBKDF2 local solo tras validar centralmente la cuenta.
- Resolución de cuentas existentes: tras autenticar en Windows, se busca primero por `CoordinatorUserId`; si no existe asociación, se permite mapear una sola cuenta local cuyo nombre normalizado coincida, manteniendo su `UserId` e historiales. Una discrepancia entre usuario central y cuenta local asociada falla sin guardar. Este proceso solo vincula identidad; nunca importa tablas ni combina operaciones de negocio locales.
- `ICoordinatorOperationSender` envía un elemento Outbox solo si el dispositivo está vinculado, la sesión central está vigente, el usuario central coincide con el mapeo de la cuenta local autora y esa misma cuenta tiene sesión activa. El cuerpo conserva el campo `actorUserId` por compatibilidad y envía el ID central asociado; la autoridad real sigue siendo `X-User-Session`, del que el coordinador obtiene y registra el actor. El remitente valida la respuesta y devuelve el estado/sequence; el worker que recorre y confirma la cola aún queda pendiente.
- El puerto 5080 es un valor inicial configurable, no un compromiso de despliegue; la autoridad de cuentas será el coordinador y los dispositivos deberán vincularse por un Administrador. Los detalles de flujo de alta inicial y emparejamiento quedan para el resto de T7.1.
