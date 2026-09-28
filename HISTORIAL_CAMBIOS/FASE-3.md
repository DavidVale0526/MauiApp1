# Fase 3 — Movimientos, compras y salidas no comerciales

Este archivo registra la primera tarea de la fase. Las tareas siguientes se añadirán cuando se implementen y verifiquen.

## T3.1 — Modelar movimientos de inventario

### Creados

- `MauiApp1.Domain/Inventory/InventoryMovementType.cs` — tipos de entrada de compra, salida por venta y otra salida.
- `MauiApp1.Domain/Inventory/NegativeStockPolicy.cs` — opciones explícitas `Reject` y `Allow`; no se elige un valor predeterminado mientras la regla de negocio siga pendiente.
- `MauiApp1.Application/Inventory/InventoryMovementLineDraft.cs`, `InventoryOperationDraft.cs`, `InventoryOperationResult.cs` — contrato de una operación de inventario y su resultado.
- `MauiApp1.Application/Inventory/IInventoryMovementService.cs` — contrato de aplicación para aplicar una operación.
- `MauiApp1.Application/Inventory/InventoryOperationPayload.cs`, `InventoryMovementPayloadLine.cs` — contenido persistible para sincronización futura.
- `MauiApp1.Infrastructure/Persistence/Entities/InventoryMovementEntity.cs` — línea persistente del historial.
- `MauiApp1.Infrastructure/Products/EfInventoryMovementService.cs` — validación, actualización atómica de saldo, historial y Outbox, y protección de reintentos.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260928060110_InventoryMovementLedger.cs` y `.Designer.cs` — tabla, checks, índices y relaciones del historial.

### Modificados

- `MauiApp1.Infrastructure/Persistence/LocalDbContext.cs` — mapeo EF, claves únicas, restricciones e índices de `InventoryMovements`.
- `MauiApp1.Infrastructure/Persistence/LocalPersistenceServiceCollectionExtensions.cs` — registro de `IInventoryMovementService`.
- `MauiApp1.Infrastructure/Persistence/Migrations/LocalDbContextModelSnapshot.cs` — modelo EF actualizado.
- `ARCHITECTURE.md` — reglas y límites de la persistencia de movimientos.
- `D:\Trabajo\Tesis\TASK.md` — T3.1 marcada completada con su evidencia.
- `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — alcance implementado y decisiones pendientes.

### Reglas implementadas

- Cada operación tiene un `OperationId` estable. Las líneas se identifican por operación y número de línea; los identificadores repetidos con contenido distinto se rechazan.
- Compra, venta y salida distinta de venta usan tipos separados. Otras salidas requieren un motivo. Las fechas se reciben en UTC y cantidades se guardan como unidades de milésimas.
- El saldo actual del producto, las líneas de historial y la operación Outbox se guardan en una transacción local. El servicio agrega movimientos y no ofrece editar/borrar movimientos existentes.
- La política de inventario negativo continúa pendiente para T3.4/T4.1; cada llamada requiere una política explícita, sin elegir una por defecto. Las entidades de compra y venta todavía no existen y enlazarán sus operaciones al `OperationId` al implementarse.
- T3.1 no incorpora pantallas ni flujos de compra/venta/salidas.

### Verificación

- `dotnet build MauiApp1.Infrastructure/MauiApp1.Infrastructure.csproj --no-restore`: correcto, 0 advertencias y 0 errores.
- Compilación MAUI Windows (`net10.0-windows10.0.19041.0`): correcta, 0 advertencias y 0 errores.
- Compilación MAUI Android (`net10.0-android`): correcta, 0 advertencias y 0 errores.
- Smoke check con base SQLite temporal: correcto para migración/FK, compra de varias líneas, saldo e historial/Outbox atómicos, reintento idempotente, rechazo de mismo identificador con contenido diferente, salida que supera existencia bajo `Reject` sin escritura parcial y motivo obligatorio.
- La prueba Android fue de compilación; aún queda pendiente una comprobación visual en dispositivo físico/emulador.

## T3.2 — Registrar compras y entradas

### Creados

- `MauiApp1.Application/Purchases/PurchaseLineDraft.cs`, `PurchaseDraft.cs`, `PurchaseResult.cs`, `IPurchaseService.cs` — contrato para registrar una compra local.
- `MauiApp1.Application/Purchases/PurchaseRecordedPayload.cs` — datos canónicos de compra enviados en Outbox.
- `MauiApp1.Infrastructure/Persistence/Entities/PurchaseEntity.cs`, `PurchaseLineEntity.cs` — cabecera y renglones persistidos.
- `MauiApp1.Infrastructure/Purchases/EfPurchaseService.cs` — validación, consolidación, idempotencia y persistencia de compra/stock/movimientos/Outbox en una sola transacción.
- `MauiApp1.Infrastructure/Inventory/InventoryMovementWriter.cs` — prepara líneas de movimiento, actualizaciones de existencia y Outbox en el contexto compartido para que las operaciones de negocio relacionadas puedan guardarse atómicamente.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260928062439_PurchaseRegistration.cs` y `.Designer.cs` — tablas `Purchases` y `PurchaseLines`, restricciones, índices y relaciones.
- `MauiApp1/Presentation/ViewModels/PurchaseRegistrationViewModel.cs` — selección, validación, consolidación de cantidades y revisión de compra.
- `MauiApp1/Presentation/Views/PurchaseRegistrationPage.xaml`, `.xaml.cs` — pantalla compartida Windows/Android de registro y confirmación.

### Modificados

- `MauiApp1.Infrastructure/Persistence/LocalDbContext.cs` y `LocalPersistenceServiceCollectionExtensions.cs` — esquema y registro de servicios.
- `MauiApp1.Infrastructure/Products/EfInventoryMovementService.cs` — usa el escritor compartido para mantener comportamiento idempotente.
- `MauiApp1.Infrastructure/Persistence/Migrations/LocalDbContextModelSnapshot.cs` — modelo EF actualizado.
- `MauiApp1/MauiProgram.cs`, `MauiApp1/Presentation/Views/MainPage.xaml` y `.xaml.cs` — acceso a «Registrar compra» desde el inicio autenticado.
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — decisión, alcance y evidencia de T3.2.

### Alcance aprobado

Se persisten solamente fecha/hora UTC, producto y cantidad recibida. No se añadieron proveedor, costo, documento de proveedor, lote ni vencimiento. Solo se pueden agregar productos activos. Si se repite un producto, las cantidades se consolidan en un único renglón. Para su historial se conserva también el nombre/unidad tal como se mostraban al confirmar. El usuario puede quitar renglones antes de revisar/confirmar. Tras una falla, se conserva el mismo borrador/`OperationId` para reintentar su contenido sin modificarlo.

### Verificación

- Infraestructura: compilación correcta, 0 advertencias y 0 errores.
- MAUI Windows (`net10.0-windows10.0.19041.0`): compilación correcta, 0 advertencias y 0 errores.
- MAUI Android (`net10.0-android`): compilación correcta, 0 advertencias y 0 errores.
- Smoke check con SQLite temporal: migraciones/FK, alta de Administrador e inicio de sesión, producto activo, cantidades positivas, dos renglones del mismo producto consolidados, exactamente una cabecera/renglón/movimiento/Outbox, saldo aumentado correctamente, reintento idempotente y rechazo de contenido distinto con el mismo identificador.
- La pantalla compila para Windows y Android. La comprobación visual manual de la pantalla en Windows queda pendiente del usuario; Android físico/emulador también pendiente.

## T3.3 — Consultar historial de compras

### Creados

- `MauiApp1.Application/Purchases/PurchaseHistoryEntry.cs` — resultado de consulta con fecha UTC y renglones históricos.
- `MauiApp1/Presentation/ViewModels/PurchaseHistoryViewModel.cs` — carga, estado, formato de fecha local y cantidades.
- `MauiApp1/Presentation/Views/PurchaseHistoryPage.xaml`, `.xaml.cs` — lista de solo lectura, estado vacío, actualización manual y regreso.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260928064547_PurchaseHistorySnapshots.cs` y `.Designer.cs` — columnas para nombre/unidad históricos y backfill de compras previas desde el catálogo existente.

### Modificados

- `MauiApp1.Application/Purchases/IPurchaseService.cs`, `PurchaseLineDraft.cs`, `PurchaseRecordedPayload.cs` — lectura del historial y persistencia/sincronización de snapshots.
- `MauiApp1.Infrastructure/Purchases/EfPurchaseService.cs` — consulta ordenada sin filtros, captura/valida snapshots, conserva datos históricos después de editar el catálogo.
- `MauiApp1.Infrastructure/Persistence/Entities/PurchaseLineEntity.cs`, `LocalDbContext.cs` y `Migrations/LocalDbContextModelSnapshot.cs` — persistencia de nombre y unidad del producto al confirmar.
- `MauiApp1/MauiProgram.cs`, `MauiApp1/Presentation/Views/MainPage.xaml` y `.xaml.cs` — registro y acceso al historial desde Inicio.
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — alcance, decisión y evidencia.

### Verificación

- Infrastructure, Windows (`net10.0-windows10.0.19041.0`) y Android (`net10.0-android`) compilaron con 0 advertencias y 0 errores.
- Smoke check SQLite validó migración desde una base anterior de T3.2 y backfill de nombre/unidad; consulta ordenada; producto, cantidad consolidada y unidad histórica; persistencia del nombre/unidad originales tras cambiar el catálogo; e idempotencia después de renombrar el producto sin doble aumento de existencias.
- No se añadieron filtros ni búsqueda. Falta la comprobación visual manual en Windows por el usuario; la prueba Android físico/emulador sigue pendiente.

## T3.4 — Registrar salidas distintas de venta

### Creados

- `MauiApp1/Presentation/ViewModels/OtherExitViewModel.cs` — selección de producto, cantidad, motivos, validación del saldo y protección del borrador al reintentar.
- `MauiApp1/Presentation/Views/OtherExitPage.xaml`, `.xaml.cs` — pantalla compartida para preparar, revisar y confirmar una salida.

### Modificados

- `MauiApp1.Infrastructure/Inventory/InventoryMovementWriter.cs` — mensaje detallado de existencia insuficiente.
- `MauiApp1.Infrastructure/Products/EfInventoryMovementService.cs` — solo permite productos activos en movimientos iniciados desde el flujo local.
- `MauiApp1/MauiProgram.cs`, `MauiApp1/Presentation/Views/MainPage.xaml` y `.xaml.cs` — acceso a la página de salidas; menú principal ahora permite desplazamiento vertical en pantallas pequeñas.
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — regla de stock negativo, causas y evidencia de T3.4.

### Decisiones y reglas

- Se bloquea la salida si las existencias locales no alcanzan. T4.1 debe aplicar la misma regla; conflictos de ventas simultáneas sin LAN quedan pendientes para T7.2.
- Motivos disponibles: Vencimiento, Daño, Pérdida y Otro. Otro requiere una descripción; se guarda con prefijo `Otro:` junto al motivo.
- El flujo registra un producto por operación, ofrece revisión antes de confirmar y conserva `OperationId` y contenido si hace falta reintentar. Los fallos no dejan saldo, movimiento ni Outbox parciales.

### Verificación

- Infrastructure, MAUI Windows (`net10.0-windows10.0.19041.0`) y Android (`net10.0-android`) compilaron correctamente con 0 advertencias y 0 errores.
- Smoke check SQLite: exceso rechazado con saldo intacto y sin Outbox/movimiento parcial; motivo vacío rechazado; vencimiento y Otro con descripción aceptados; saldo y movimiento guardados una vez; reintento idempotente; mismo identificador con razón distinta rechazado.
- La revisión visual manual en Windows queda pendiente del usuario; Android físico/emulador también pendiente.
