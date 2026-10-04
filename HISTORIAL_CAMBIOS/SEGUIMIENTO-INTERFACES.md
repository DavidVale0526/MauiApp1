# Seguimiento de interfaces

Registro de las vistas adaptadas al estilo visual de Figma. Las referencias guían la apariencia; las funciones y datos siguen limitados a lo que ya existe en la aplicación.

## Estado

| Orden | Vista de la aplicación | Referencia | Estado |
|---:|---|---|---|
| 1 | Acceso e inicio de sesión (`AccessPage`) | Inicio de sesión | Adaptada; ya revisada durante los ajustes de acceso |
| 2 | Menú principal (`MainPage`) | Panel principal / Dashboard | Adaptada; el usuario indicó continuar |
| 3 | Registro de venta (`SaleRegistrationPage`) | Punto de venta | Adaptada; el usuario indicó continuar |
| 4 | Catálogo y productos (`ProductManagementPage`) | Inventario / catálogo | Adaptada; pendiente de revisión |
| 5 | Consulta de existencias (`StockConsultationPage`) | Inventario | Adaptada; pendiente de revisión |
| 6 | Categorías (`CategoryManagementPage`) | Catálogo | Adaptada; pendiente de revisión |
| 7 | Historial de ventas (`SaleHistoryPage`) | Ventas / historial | Pendiente |
| 8 | Comprobante de venta (`SaleTicketPage`) | Ticket / factura | Pendiente |
| 9 | Registro de compras (`PurchaseRegistrationPage`) | Entrada de mercancía | Pendiente |
| 10 | Historial de compras (`PurchaseHistoryPage`) | Historial de compras | Pendiente |
| 11 | Historial de movimientos (`InventoryMovementHistoryPage`) | Movimientos de inventario | Pendiente |
| 12 | Salida de inventario (`OtherExitPage`) | Salidas / mermas | Pendiente |
| 13 | Clientes y crédito (`CustomerManagementPage`) | Clientes | Pendiente |
| 14 | Pagos (`PaymentManagementPage`) | Pagos / saldos | Pendiente |
| 15 | Reportes (`ReportsPage`) | Reportes | Pendiente |
| 16 | Gestión de cuentas de cajero (`CashierSetupPage`) | Usuarios / administración | Pendiente |
| 17 | Sincronización y conexión (`SyncManagementPage`) | Panel de sincronización | Pendiente |
| 18 | Revisión de conflictos (`ConflictReviewPage`) | Sincronización | Pendiente |
| 19 | Vinculación de dispositivo (`DevicePairingPage`) | Sincronización | Pendiente |
| 20 | Lector QR de vinculación (`PairingQrScannerPage`) | Sincronización | Pendiente |

## Notas

- `StockConsultationPage` se registra por separado del catálogo porque es una pantalla independiente en la aplicación.
- No encontré una pantalla móvil dedicada al catálogo en la página `moviles` de Figma; esa vista se adapta al teléfono con el mismo contenido disponible y una disposición de una columna.
- Los diseños muestran campos, cifras, filtros y acciones que algunas vistas actuales no tienen. No se agregan a la aplicación durante esta adaptación visual.
- `CoordinatorRefreshPage` es una clase base, no una pantalla visible, por lo que no se marca como vista.
