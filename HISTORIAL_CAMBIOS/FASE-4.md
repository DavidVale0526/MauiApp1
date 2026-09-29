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
- El usuario confirmó que la pantalla funciona manualmente en Windows y en su teléfono Android físico el 2026-09-28.

## T4.2 — Consultar historial de ventas

### Creados

- `MauiApp1.Application/Sales/SaleHistoryEntry.cs` — resultado de consulta para cabecera y renglones de venta históricos.
- `MauiApp1/Presentation/ViewModels/SaleHistoryViewModel.cs` — carga, formato de fecha local, renglones y totales.
- `MauiApp1/Presentation/Views/SaleHistoryPage.xaml`, `.xaml.cs` — lista de ventas confirmadas, estado vacío/error, actualización manual y regreso.

### Modificados

- `MauiApp1.Application/Sales/ISaleService.cs` — contrato de consulta del historial.
- `MauiApp1.Infrastructure/Sales/EfSaleService.cs` — consulta autenticada/no tracking, ordenada por fecha descendente, con usuario y renglones snapshots.
- `MauiApp1/MauiProgram.cs`, `MauiApp1/Presentation/Views/MainPage.xaml` y `.xaml.cs` — registro DI y acceso a «Historial de ventas».
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — alcance y evidencia de T4.2.

### Reglas y verificación

- La pantalla muestra fecha/hora local, usuario que registró, referencia corta del `OperationId`, productos con nombre/unidad históricos, cantidad, precio unitario aplicado, importe de cada línea y total.
- La consulta es de solo lectura y requiere una sesión local. No habilita cambios, eliminación, anulaciones, filtros, pagos ni comprobantes.
- Infrastructure, MAUI Windows y Android compilaron con 0 advertencias y 0 errores.
- Smoke check SQLite pasó historial vacío/consultable, sesión requerida, orden de dos ventas, usuario correcto, importes/snapshots tras renombrar y reprecificar el producto y stock sin cambios por consultar.
- El usuario confirmó manualmente que el historial funciona.

## T4.3 — Ticket interno de venta

### Creados y modificados

- `MauiApp1/Presentation/ViewModels/SaleTicketViewModel.cs` — presentación de fecha, actor, referencia, moneda y snapshots de las líneas.
- `MauiApp1/Presentation/Views/SaleTicketPage.xaml`, `.xaml.cs` — ticket interno visible en pantalla, navegación de regreso.
- `MauiApp1/Presentation/Views/SaleRegistrationPage.xaml.cs`, `SaleRegistrationViewModel.cs` — abrir el ticket tras persistir la venta confirmada.
- `MauiApp1/Presentation/Views/SaleHistoryPage.xaml`, `.xaml.cs` — acceso para volver a consultar el ticket desde historial.
- `MauiApp1/ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — decisión y alcance aprobados.

### Decisión y alcance

- Se aprobó ticket interno no fiscal, solo en pantalla, con referencia corta del `OperationId` y snapshots históricos de la venta.
- La moneda se muestra como córdobas nicaragüenses (`C$`), incluido el total y cada importe de línea.
- No se agregan impresión, impuestos, datos fiscales ni información de cobro. Una factura fiscal se mantiene fuera del alcance hasta que se definan sus requisitos.

### Verificación

- Infrastructure, MAUI Windows (`net10.0-windows10.0.19041.0`) y Android (`net10.0-android`) compilaron con 0 advertencias y 0 errores tras el ajuste de moneda.
- Revisión visual manual del usuario pendiente.
