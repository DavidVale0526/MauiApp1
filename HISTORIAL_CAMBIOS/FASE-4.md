# Fase 4 — Ventas e historial

## T4.1 — Registrar ventas

### Creados

- `MauiApp1.Application/Sales/SaleDraft.cs` — borrador de venta y renglones con snapshots, cantidad y precio aplicado.
- `MauiApp1.Application/Sales/SaleResult.cs`, `ISaleService.cs` — contrato y resultado de registro idempotente.
- `MauiApp1.Application/Sales/SaleRecordedPayload.cs` — cabecera, renglones, snapshots y total que se conserva en Outbox.
- `MauiApp1.Application/Sales/SaleAmountCalculator.cs` — regla única de importe por renglón en centavos.
- `MauiApp1.Infrastructure/Persistence/Entities/SaleEntity.cs`, `SaleLineEntity.cs` — persistencia de venta y renglones.
- `MauiApp1.Infrastructure/Sales/EfSaleService.cs` — consolidación, validación, cálculo, control de stock, persistencia atómica e idempotencia.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260928120000_SalesRegistration.cs` y `.Designer.cs` — tablas, restricciones, índices y relaciones para ventas.
- `MauiApp1/Presentation/ViewModels/SaleRegistrationViewModel.cs` — catálogo disponible, validación de cantidades y stock, carrito, resumen/total y reintento seguro.
- `MauiApp1/Presentation/Views/SaleRegistrationPage.xaml`, `.xaml.cs` — selección, revisión y confirmación del registro de ventas.

### Modificados

- `MauiApp1.Application/Inventory/InventoryMovementLineDraft.cs`, `MauiApp1.Infrastructure/Inventory/InventoryMovementWriter.cs` — permiten pasar el snapshot de venta al movimiento de inventario compartido.
- `MauiApp1.Infrastructure/Persistence/LocalDbContext.cs`, `LocalPersistenceServiceCollectionExtensions.cs` y `Migrations/LocalDbContextModelSnapshot.cs` — modelo de ventas y registro de `ISaleService`.
- `MauiApp1/MauiProgram.cs`, `MauiApp1/Presentation/Views/MainPage.xaml` y `.xaml.cs` — navegación y DI para «Registrar venta».
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — decisiones, alcance y evidencia de T4.1.
- `HISTORIAL_CAMBIOS/README.md` — enlace a esta fase.

### Decisiones implementadas

- Se conserva nombre, unidad y precio aplicados al agregar el producto; cada operación registra fecha UTC y usuario.
- El mismo producto se consolida en una línea. La cantidad total no puede superar la existencia local vigente; los artículos inactivos no se venden.
- El precio se almacena en centavos y la cantidad en milésimas. Cada renglón se calcula y redondea a centavos con mitades hacia arriba; el total suma los renglones ya redondeados.
- La venta, líneas, `SaleExit`, saldo de producto y Outbox `Sale.Recorded` se guardan en una sola operación local. La repetición exacta devuelve éxito sin descontar stock de nuevo; contenido alterado con el mismo `OperationId` se rechaza.
- No se guardan cobros, medios de pago, clientes/crédito, impuestos, descuentos o comprobantes en T4.1.

### Verificación

- Infrastructure, MAUI Windows (`net10.0-windows10.0.19041.0`) y Android (`net10.0-android`) compilaron con 0 advertencias y 0 errores.
- Smoke check de SQLite: migración/relaciones; compra de stock; venta con dos renglones repetidos; consolidación; redondeo de mitad hacia arriba; persistencia de snapshots; descuento único en reintento; rechazo de contenido distinto y de stock insuficiente sin cabecera, movimiento ni cambio de saldo parcial.
- Falta la revisión visual manual en Windows por el usuario; Android físico/emulador queda pendiente.
