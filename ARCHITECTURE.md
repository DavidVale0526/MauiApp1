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
- Los identificadores de producto y categoría son Guid (`ProductId` y `CategoryId`). Las fechas se manejan como `DateTime` UTC y se guardan en SQLite como ticks enteros, para preservar el instante UTC y ordenar con precisión. Los importes se representan en unidades menores enteras con dos decimales; las cantidades se representan en milésimas enteras con tres decimales. Cada producto conserva su propia unidad editable. La moneda sigue pendiente de la tarea de comprobantes.
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
