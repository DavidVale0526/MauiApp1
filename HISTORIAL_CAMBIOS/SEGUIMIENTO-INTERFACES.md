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
| 7 | Historial de ventas (`SaleHistoryPage`) | Ventas / historial | Adaptada; pendiente de revisión |
| 8 | Comprobante de venta (`SaleTicketPage`) | Ticket / factura | Adaptada; pendiente de revisión |
| 9 | Registro de compras (`PurchaseRegistrationPage`) | Entrada de mercancía | Adaptada; pendiente de revisión |
| 10 | Historial de compras (`PurchaseHistoryPage`) | Historial de compras | Adaptada; pendiente de revisión |
| 11 | Historial de movimientos (`InventoryMovementHistoryPage`) | Movimientos de inventario | Adaptada; pendiente de revisión |
| 12 | Salida de inventario (`OtherExitPage`) | Salidas / mermas | Adaptada; pendiente de revisión |
| 13 | Clientes y crédito (`CustomerManagementPage`) | Clientes | Adaptada; pendiente de revisión |
| 14 | Pagos (`PaymentManagementPage`) | Cobro y pagos | Adaptada; pendiente de revisión |
| 15 | Reportes (`ReportsPage`) | Reportes | Pendiente |
| 16 | Gestión de cuentas de cajero (`CashierSetupPage`) | Usuarios / administración | Pendiente |
| 17 | Sincronización y conexión (`SyncManagementPage`) | Panel de sincronización | Pendiente |
| 18 | Revisión de conflictos (`ConflictReviewPage`) | Sincronización | Pendiente |
| 19 | Vinculación de dispositivo (`DevicePairingPage`) | Sincronización | Pendiente |
| 20 | Lector QR de vinculación (`PairingQrScannerPage`) | Sincronización | Pendiente |

## Notas

- La página `moviles` de Figma incluye una barra inferior persistente con Inicio, Ventas, Inventario, Reportes y Más. Se añadió en Android a las vistas de operación; Inventario y Reportes llevan a las consultas existentes, mientras Más reúne el resto de módulos y acciones disponibles. En escritorio se mantiene la navegación lateral.
- `StockConsultationPage` se registra por separado del catálogo porque es una pantalla independiente en la aplicación.
- Para `InventoryMovementHistoryPage` se revisó el diseño de escritorio `movimientos de invetario` en Figma; no hay una vista específica equivalente en `moviles`. Se adaptó el listado de datos existentes y se omitieron indicadores, filtros y exportación que la aplicación aún no ofrece.
- Para `OtherExitPage` se revisó en `interfaces` el formulario modal `agregar una nueva salida`; no hay un diseño equivalente en `moviles`. Se adaptó el formulario actual y se mantuvieron solo los motivos, campos y confirmación que ya están implementados.
- Para `CustomerManagementPage` se buscaron referencias de clientes y crédito en ambas páginas de Figma; no hay una pantalla dedicada. Las menciones de cliente aparecen dentro de ventas y compras, por lo que se aplicaron los estilos visuales generales sin agregar filtros ni acciones.
- Para `PaymentManagementPage` se buscaron pagos y saldos en `interfaces` y `moviles`. Figma contiene elementos de cobro en ventas y pagos en movimientos de caja, pero no una pantalla dedicada para registrar abonos a deudas de clientes. Se aplicó el estilo visual general y se conservaron las funciones actuales de selección de cuenta, confirmación, registro e historial.
- No encontré una pantalla móvil dedicada al catálogo en la página `moviles` de Figma; esa vista se adapta al teléfono con el mismo contenido disponible y una disposición de una columna.
- Los diseños muestran campos, cifras, filtros y acciones que algunas vistas actuales no tienen. No se agregan a la aplicación durante esta adaptación visual.
- `CoordinatorRefreshPage` es una clase base, no una pantalla visible, por lo que no se marca como vista.
