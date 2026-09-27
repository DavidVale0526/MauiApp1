# Arquitectura inicial

La solución mantiene una sola aplicación .NET MAUI para Windows y Android. Las reglas compartidas se escribirán una vez; el código específico de plataforma se limitará a integraciones que MAUI no pueda resolver de forma común.

## Proyectos y dependencias

- `MauiApp1`: presentación MAUI, vistas, ViewModels y punto de composición de dependencias.
- `MauiApp1.Application`: casos de uso y contratos de aplicación; referencia el dominio.
- `MauiApp1.Domain`: reglas y tipos del negocio sin dependencia de interfaz, base de datos o plataforma.
- `MauiApp1.Infrastructure`: implementaciones de persistencia y comunicación; implementará contratos de Application y podrá usar Domain.

La dirección de dependencias es `Infrastructure -> Application -> Domain`. La aplicación MAUI referencia Application e Infrastructure para conectar implementaciones al iniciar. No se añaden todavía modelos, servicios de negocio, base de datos ni comunicación LAN: pertenecen a los pasos siguientes y no están definidos en T1.1.

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
- Los identificadores de producto y categoría son Guid (`ProductId` y `CategoryId`). Las fechas se manejan como `DateTime` UTC y se guardan en SQLite como ticks enteros, para preservar el instante UTC y ordenar con precisión. Los importes se representan en unidades menores enteras con dos decimales y las cantidades en milésimas enteras con tres decimales; la moneda y unidad específicas se decidirán en los módulos que las necesiten.
- Se usa EF Core 10.0.12 y `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5. La versión explícita del bundle evita restaurar la versión 2.1.11 señalada por la auditoría de NuGet.

La base central no se comparte como archivo con las aplicaciones. Se agregará al servicio coordinador Windows cuando se implemente ese módulo; en esta etapa cada app solo accede a su archivo privado local.
