# Fase 2 — Categorías y catálogo de productos

Rutas relativas a la carpeta `D:\Trabajo\Tesis\MauiApp1`.

## T2.1 — Categorías

### Creados

- `MauiApp1.Application/Categories/CategorySummary.cs` — elemento de consulta con ID y nombre.
- `MauiApp1.Application/Categories/CategoryOperationResult.cs` — resultado de validación/operación para UI.
- `MauiApp1.Application/Categories/ICategoryService.cs` — contrato de consulta, alta, renombrado y eliminación.
- `MauiApp1.Application/Categories/CategoryChangedPayload.cs`, `CategoryDeletedPayload.cs` — carga durable mínima para sincronizar luego cambios de categoría.
- `MauiApp1.Infrastructure/Persistence/Entities/CategoryEntity.cs` — representación persistente con ID, nombre y nombre normalizado.
- `MauiApp1.Infrastructure/Categories/EfCategoryService.cs` — CRUD local, regla de nombres, validación de sesión y guardado coordinado con Outbox.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260928044813_CategoryCatalog.cs` y `20260928044813_CategoryCatalog.Designer.cs` — tabla `Categories` e índice único para el nombre normalizado.
- `MauiApp1/Presentation/ViewModels/CategoryManagementViewModel.cs` — estado y acciones de la pantalla.
- `MauiApp1/Presentation/Views/CategoryManagementPage.xaml`, `.xaml.cs` — vista MAUI de consulta, alta, edición, confirmación y eliminación.

### Modificados

- `MauiApp1.Infrastructure/Persistence/LocalDbContext.cs` — mapeo e índice único de categorías.
- `MauiApp1.Infrastructure/Persistence/LocalPersistenceServiceCollectionExtensions.cs` — registro del servicio de categorías.
- `MauiApp1/MauiProgram.cs` — registro de página y ViewModel.
- `MauiApp1/Presentation/Views/MainPage.xaml`, `.xaml.cs` — acceso a categorías para una sesión autenticada.
- `ARCHITECTURE.md` — reglas y persistencia del catálogo.
- `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — decisiones aprobadas y pendientes de catálogo.
- `D:\Trabajo\Tesis\TASK.md` — checklist, evidencia y FK restrictiva requerida en T2.2.

### Decisiones

Nombre requerido, recortado y único sin distinguir mayúsculas/minúsculas. Renombrar está permitido aunque se use. Eliminar se bloquea si hay productos asociados. La FK real queda a cargo de T2.2, junto con la tabla de productos.

### Verificación

Comprobación temporal sobre SQLite validó alta/consulta/renombrado/eliminación y que la restricción FK bloquea el borrado sin dejar una fila Outbox parcial. La relación FK de producto se simuló solo en la base temporal porque `Products` pertenece a T2.2. Windows y Android compilaron con cero advertencias y cero errores. No se probó visualmente en un teléfono ni emulador; no estaban disponibles.

### Ajuste de navegación

- `MauiApp1/Presentation/Views/CategoryManagementPage.xaml` muestra un botón «← Volver» para regresar a la pantalla principal.
- Verificación: compilaciones Windows y Android correctas, con 0 advertencias y 0 errores.

## T2.2 — Productos

### Creados

- `MauiApp1.Application/Products/ProductDraft.cs`, `ProductSummary.cs`, `ProductOperationResult.cs`, `IProductService.cs` — contratos de edición, consulta y resultado.
- `MauiApp1.Application/Products/ProductChangedPayload.cs` — snapshot completo usado por Outbox.
- `MauiApp1.Infrastructure/Persistence/Entities/ProductEntity.cs` — entidad persistente de catálogo.
- `MauiApp1.Infrastructure/Products/EfProductService.cs` — CRUD local, validación, conservación de stock y Outbox atómica.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260928052640_ProductCatalog.cs` y `.Designer.cs` — tabla `Products`, índices y FK restrictiva a `Categories`.
- `MauiApp1/Presentation/ViewModels/ProductManagementViewModel.cs` — estado, validación de entrada y acciones de la pantalla.
- `MauiApp1/Presentation/Views/ProductManagementPage.xaml`, `.xaml.cs` — alta, consulta, edición, desactivación/reactivación y regreso a la pantalla principal.

### Modificados

- `MauiApp1.Infrastructure/Persistence/LocalDbContext.cs` — mapeo de productos y clave foránea `DeleteBehavior.Restrict`.
- `MauiApp1.Infrastructure/Persistence/LocalPersistenceServiceCollectionExtensions.cs` — registro de `IProductService`.
- `MauiApp1.Infrastructure/Persistence/Migrations/LocalDbContextModelSnapshot.cs` — modelo EF actualizado.
- `MauiApp1/MauiProgram.cs` — registro de página y ViewModel.
- `MauiApp1/Presentation/Views/MainPage.xaml`, `.xaml.cs` — botón de acceso al catálogo de productos.
- `MauiApp1.Domain/Values/Quantity.cs` — documentación de cantidades ligadas a la unidad de producto.
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — reglas aprobadas y evidencia.

### Decisiones

Nombre y unidad editables, obligatorios y recortados; no se impuso unicidad de nombre ni se añadió SKU. Precio no negativo con 2 decimales; existencia y mínimo no negativos con 3. La existencia inicia en cero y no se edita desde el catálogo. Se desactiva/reactiva conservando producto e identificador; las entradas se implementarán en T3. La unidad es texto editable por producto. Sin campos adicionales no aprobados.

### Verificación

Migración aplicada a SQLite temporal. Smoke check verificó valores exactos, recorte/validación de nombre y unidad, rechazo de precios/mínimos negativos y categoría inexistente, creación/edición con existencia intacta, desactivación/reactivación con retención, bloqueo FK al borrar categoría en uso y que el fallo no agregó Outbox parcial. Se conservaron cinco operaciones Outbox correspondientes a cambios correctos. Windows y Android compilaron con 0 advertencias y 0 errores. El usuario confirmó visualmente en Windows que la página carga y muestra un producto con categoría, precio, unidad, existencia y mínimo. No se hizo prueba visual Android por falta de emulador/dispositivo.


