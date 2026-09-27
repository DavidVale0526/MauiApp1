# Fase 1 — Base multiplataforma, persistencia e inicio de sesión

Rutas relativas a la carpeta `D:\Trabajo\Tesis\MauiApp1`.

## T1.1 — Base MAUI y arquitectura

### Creados o incorporados

- `MauiApp1.slnx` — solución con la aplicación y bibliotecas compartidas.
- `MauiApp1/MauiApp1.csproj` — aplicación MAUI para Windows y Android.
- `MauiApp1/MauiProgram.cs` — composición e inyección de dependencias.
- `MauiApp1/App.xaml`, `MauiApp1/App.xaml.cs` — arranque MAUI.
- `MauiApp1/AppShell.xaml`, `MauiApp1/AppShell.xaml.cs` — shell nativo.
- `MauiApp1/Presentation/Views/MainPage.xaml`, `MauiApp1/Presentation/Views/MainPage.xaml.cs` — vista inicial.
- `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs` — estado de presentación separado de la vista.
- `MauiApp1.Domain/MauiApp1.Domain.csproj` — proyecto de dominio.
- `MauiApp1.Application/MauiApp1.Application.csproj` — proyecto de aplicación.
- `MauiApp1.Infrastructure/MauiApp1.Infrastructure.csproj` — proyecto de infraestructura.
- `ARCHITECTURE.md` — capas, dependencias, plataformas y comandos de compilación.
- `.config/dotnet-tools.json` — herramientas locales de desarrollo (se usó para migraciones en T1.2).

### Verificación registrada

Compilación para Windows y Android; ambas terminaron con cero advertencias y cero errores. No se abrió ni instaló la app en dispositivos/emuladores.

## T1.2 — Almacenamiento local confiable

### Creados

- `MauiApp1.Domain/Identifiers/ProductId.cs` — identificador estable de producto.
- `MauiApp1.Domain/Identifiers/CategoryId.cs` — identificador estable de categoría.
- `MauiApp1.Domain/Values/MoneyAmount.cs` — importe entero en unidades menores, precisión de dos decimales.
- `MauiApp1.Domain/Values/Quantity.cs` — cantidad entera en milésimas, precisión de tres decimales.
- `MauiApp1.Application/Storage/ILocalDatabaseInitializer.cs` — contrato de inicialización local.
- `MauiApp1.Application/Storage/ILocalDeviceIdentity.cs` — contrato de identidad estable de instalación.
- `MauiApp1.Application/Storage/IOperationOutbox.cs` — contrato de cola de operaciones y sus registros.
- `MauiApp1.Application/Storage/ILocalUnitOfWork.cs` — contrato de confirmación transaccional.
- `MauiApp1.Infrastructure/Persistence/LocalDbContext.cs` — contexto EF Core y configuración del esquema.
- `MauiApp1.Infrastructure/Persistence/Entities/DeviceIdentityEntity.cs` — identidad local persistida.
- `MauiApp1.Infrastructure/Persistence/Entities/OutboxMessageEntity.cs` — operación pendiente/confirmada persistida.
- `MauiApp1.Infrastructure/Persistence/LocalDatabaseInitializer.cs` — aplica migraciones y crea/recupera la identidad local.
- `MauiApp1.Infrastructure/Persistence/EfOperationOutbox.cs` — persistencia idempotente, reintentos y acuse durable.
- `MauiApp1.Infrastructure/Persistence/LocalPersistenceServiceCollectionExtensions.cs` — registro de persistencia en DI.
- `MauiApp1.Infrastructure/Persistence/LocalDbContextFactory.cs` — creación del contexto para herramientas EF.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260927091653_InitialLocalSchema.cs` — esquema SQLite inicial.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260927091653_InitialLocalSchema.Designer.cs` — metadatos de migración.
- `MauiApp1.Infrastructure/Persistence/Migrations/LocalDbContextModelSnapshot.cs` — versión de referencia del esquema.

### Modificados

- `MauiApp1/MauiProgram.cs` — registra e inicializa la persistencia local al arrancar.
- `MauiApp1.Infrastructure/MauiApp1.Infrastructure.csproj` — versiones explícitas de EF Core SQLite y SQLitePCLRaw.
- `ARCHITECTURE.md` — ubicación de la base, migraciones, precisión, outbox y reglas de uso.
- `D:\Trabajo\Tesis\TASK.md` — evidencia y casillas de T1.2 completadas.

### Verificación registrada

Migración inicial aplicada a una base temporal; comprobación local verificó identidad estable, deduplicación, recuperación de operación pendiente, reintento y acuse persistido. Compilación final de Windows y Android con cero advertencias y cero errores. No se abrió ni instaló la aplicación en dispositivos/emuladores.

### T1.3 — Acceso local autenticado

#### Creados

- `MauiApp1.Domain/Identifiers/UserId.cs` — identidad estable local de usuario.
- `MauiApp1.Domain/Users/UserRole.cs`, `MauiApp1.Domain/Users/AuthenticatedUser.cs` — roles confirmados y usuario de sesión.
- `MauiApp1.Application/Authentication/IAuthenticationService.cs`, `MauiApp1.Application/Authentication/AuthenticationResult.cs` — contrato del acceso y resultado sin credenciales.
- `MauiApp1.Infrastructure/Persistence/Entities/UserAccountEntity.cs` — cuenta local con nombre normalizado, verificador de contraseña, rol y fecha UTC.
- `MauiApp1.Infrastructure/Authentication/Pbkdf2PasswordHasher.cs` — hash con sal aleatoria y PBKDF2-HMAC-SHA256.
- `MauiApp1.Infrastructure/Authentication/LocalAuthenticationService.cs` — alta inicial única del Administrador, alta de Cajero autorizada para Administrador, acceso, sesión en memoria y cierre.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260927094231_LocalUserAuthentication.cs` y `.Designer.cs` — tabla e índice único de cuentas.
- `MauiApp1/Presentation/ViewModels/AccessPageViewModel.cs`, `MauiApp1/Presentation/Views/AccessPage.xaml`, `.xaml.cs` — alta inicial e inicio de sesión.
- `MauiApp1/Presentation/ViewModels/CashierSetupViewModel.cs`, `MauiApp1/Presentation/Views/CashierSetupPage.xaml`, `.xaml.cs` — alta básica de cuentas Cajero.

#### Modificados

- `MauiApp1.Infrastructure/Persistence/LocalDbContext.cs`, `LocalPersistenceServiceCollectionExtensions.cs` — mapeo del esquema y registro del servicio de autenticación.
- `MauiApp1/MauiProgram.cs` — registro de vistas y ViewModels.
- `MauiApp1/AppShell.xaml.cs` — cambia la página raíz entre acceso y página principal según la sesión.
- `MauiApp1/Presentation/Views/MainPage.xaml`, `.xaml.cs`, `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs` — identidad/rol actual, salida y acceso de Administrador al alta de Cajero.
- `ARCHITECTURE.md` — decisiones de autenticación, política de contraseña y límite local por dispositivo.
- `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — precisa el alta local de Administrador/Cajero en T1.3 y el acceso compartido al llegar a T7.1.
- `D:\Trabajo\Tesis\TASK.md` — evidencia y tareas T1.3 completadas.

#### Verificación registrada

La comprobación temporal aplicó la migración y verificó alta única del Administrador, alta de Cajero solo por Administrador, rechazo de duplicados e intentos de alta no autorizados, acceso de ambos tipos de cuenta, error genérico por credenciales incorrectas, cierre de sesión, hash con sal de 600,000 iteraciones y aislamiento en otra base/dispositivo. Windows y Android compilaron con cero advertencias y cero errores. No se abrió la app en dispositivos/emuladores; esa verificación corresponde a T1.4.

### T1.4 — Verificación multiplataforma (parcial; Android físico pendiente)

- Windows compiló correctamente y se abrió hasta la pantalla nativa de configuración inicial. No se introdujo ni guardó una contraseña en el perfil real del usuario.
- Android compiló correctamente y generó `MauiApp1/MauiApp1/bin/Debug/net10.0-android/com.companyname.mauiapp1-Signed.apk`. La instalación y apertura en el teléfono físico quedan reservadas para la verificación final del usuario.
- Un verificador temporal creó una cuenta y una operación pendiente en un primer proceso, terminó ese proceso y recuperó/inició sesión/leyó la operación en un segundo proceso. No utilizó clientes de red; no se apagaron adaptadores del equipo.
- Los directorios y bases temporales del verificador se eliminaron. T1.4 no se declara terminada hasta recibir la comprobación del teléfono y el escenario offline con conectividad deshabilitada.
