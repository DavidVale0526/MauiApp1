# Fase 5 — Clientes y crédito

## T5.1 — Registrar clientes y ventas a crédito

### Creados

- `MauiApp1.Application/Customers/CustomerSummary.cs`, `ICustomerService.cs` — lectura de clientes/saldos y alta.
- `MauiApp1.Infrastructure/Customers/EfCustomerService.cs` — validación de nombre, unicidad normalizada, alta atómica con Outbox y consulta de saldos.
- `MauiApp1.Infrastructure/Persistence/Entities/CustomerEntity.cs`, `ReceivableEntity.cs` — cliente y cuenta por cobrar vinculada a venta.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260929040220_CustomerCreditSales.cs` y `.Designer.cs` — migración para clientes, asociación opcional de ventas, indicador crédito y cuentas por cobrar.
- `MauiApp1/Presentation/ViewModels/CustomerManagementViewModel.cs` — registro/consulta de clientes y saldos.
- `MauiApp1/Presentation/Views/CustomerManagementPage.xaml`, `.xaml.cs` — página «Clientes y crédito».

### Modificados

- `MauiApp1.Application/Sales/SaleDraft.cs`, `SaleRecordedPayload.cs`, `SaleHistoryEntry.cs` — campos de cliente, modalidad y vencimiento.
- `MauiApp1.Infrastructure/Persistence/LocalDbContext.cs`, `LocalDbContextModelSnapshot.cs` — entidades, claves, restricciones e índices.
- `MauiApp1.Infrastructure/Sales/EfSaleService.cs` — validación de crédito, creación atómica de deuda y datos históricos.
- `MauiApp1.Infrastructure/Persistence/LocalPersistenceServiceCollectionExtensions.cs` — registro de servicio de clientes.
- `MauiApp1/MauiProgram.cs`, `MauiApp1/Presentation/Views/MainPage.xaml`, `.xaml.cs` — registro de dependencias y entrada al módulo.
- `MauiApp1/Presentation/ViewModels/SaleRegistrationViewModel.cs`, `MauiApp1/Presentation/Views/SaleRegistrationPage.xaml` — selector de crédito/cliente y vencimiento en revisión.
- `MauiApp1/Presentation/ViewModels/SaleHistoryViewModel.cs`, `MauiApp1/Presentation/Views/SaleHistoryPage.xaml`, `MauiApp1/Presentation/ViewModels/SaleTicketViewModel.cs`, `MauiApp1/Presentation/Views/SaleTicketPage.xaml` — muestran modalidad, cliente y vencimiento.
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — decisiones y evidencia de T5.1.

### Decisiones aplicadas

- Nombre de cliente obligatorio y único tras recortar extremos y normalizar mayúsculas/minúsculas; teléfono opcional.
- Vencimiento calculado a 15 días de la fecha/hora UTC de venta.
- El monto original y saldo pendiente inician igual al total de la venta. Los pagos/abonos se implementan en T5.2.
- El total a crédito, cliente, vencimiento, descuento de inventario, movimiento y Outbox se guardan atómicamente; el reintento no duplica movimientos ni deuda.

### Verificación

- Windows (`net10.0-windows10.0.19041.0`) y Android (`net10.0-android`) compilaron con 0 advertencias y 0 errores.
- Smoke check SQLite: migración, cliente único normalizado, teléfono opcional, venta de contado de regresión, venta a crédito y vencimiento a 15 días, saldo/historial/inventario consistentes e idempotencia.
- Revisión visual manual de las pantallas por el usuario pendiente.

## T5.2 — Registrar pagos y consultar saldos

### Creados

- `MauiApp1.Application/Credit/CreditAccountSummary.cs`, `PaymentDraft.cs`, `PaymentHistoryEntry.cs`, `PaymentResult.cs`, `IPaymentService.cs` — contratos de cuentas, pagos y resultados.
- `MauiApp1.Infrastructure/Credit/EfPaymentService.cs` — validación, escritura idempotente, reducción de saldo, Outbox e historial.
- `MauiApp1.Infrastructure/Persistence/Entities/PaymentEntity.cs` — línea inmutable de pago.
- `MauiApp1.Infrastructure/Persistence/Migrations/20260929042014_CreditPayments.cs` y `.Designer.cs` — tabla de pagos, claves foráneas, índice e importe positivo.
- `MauiApp1/Presentation/ViewModels/PaymentManagementViewModel.cs` — selección de deuda, revisión, confirmación, reintento e historial.
- `MauiApp1/Presentation/Views/PaymentManagementPage.xaml`, `.xaml.cs` — consulta y registro de pagos con confirmación.

### Modificados

- `MauiApp1.Infrastructure/Persistence/LocalDbContext.cs`, `LocalDbContextModelSnapshot.cs`, `LocalPersistenceServiceCollectionExtensions.cs` — entidad, concurrencia optimista y DI.
- `MauiApp1/MauiProgram.cs`, `MauiApp1/Presentation/Views/MainPage.xaml`, `.xaml.cs` — registro y navegación de la pantalla.
- `TASK.md`, `PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md`, `ARCHITECTURE.md` — reglas confirmadas y evidencia.

### Decisiones aplicadas

- Se permiten abonos parciales positivos; se rechaza cualquier monto mayor al saldo pendiente.
- Los pagos guardan monto y fecha/hora UTC automática, usuario, deuda y un identificador idempotente. No guardan método de pago.
- Los pagos son inmutables y no se editan ni eliminan desde la interfaz.
- Pago, actualización del saldo y Outbox se confirman atómicamente. Un identificador repetido con mismo contenido se reconoce; con contenido distinto se rechaza.

### Verificación

- Windows y Android compilaron con 0 advertencias y 0 errores.
- Smoke check SQLite: migración, dos abonos parciales, sobrepago rechazado sin cambios, liquidación a cero, historial, reintento idempotente y contenido alterado rechazado.
- Revisión visual manual de la pantalla T5.2 por el usuario pendiente.
