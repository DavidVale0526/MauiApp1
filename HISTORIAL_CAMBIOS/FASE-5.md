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
