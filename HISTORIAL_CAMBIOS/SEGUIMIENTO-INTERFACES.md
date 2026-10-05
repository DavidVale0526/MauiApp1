# Seguimiento de interfaces

Registro de las vistas adaptadas al estilo visual de Figma. Las referencias guían la apariencia; las funciones y datos siguen limitados a lo que ya existe en la aplicación.

## Estado

| Orden | Vista de la aplicación | Referencia | Estado |
|---:|---|---|---|
| 1 | Acceso e inicio de sesión (`AccessPage`) | Inicio de sesión | Adaptada; ya revisada durante los ajustes de acceso |
| 2 | Menú principal (`MainPage`) | Panel principal / Dashboard | Adaptada; el usuario indicó continuar |
| 3 | Registro de venta (`SaleRegistrationPage`) | Referencias nuevas de catálogo y carrito de Windows / Android | Rediseñada; pendiente de revisión |
| 4 | Catálogo y productos (`ProductManagementPage`) | Inventario / catálogo | Adaptada; pendiente de revisión |
| 5 | Consulta de existencias (`StockConsultationPage`) | Inventario | Adaptada; pendiente de revisión |
| 6 | Categorías (`CategoryManagementPage`) | Catálogo | Adaptada; pendiente de revisión |
| 7 | Historial de ventas (`SaleHistoryPage`) | Ventas / historial | Adaptada; pendiente de revisión |
| 8 | Comprobante de venta (`SaleTicketPage`) | Referencia nueva de comprobante de venta | Rediseñada; pendiente de revisión |
| 9 | Registro de compras (`PurchaseRegistrationPage`) | Entrada de mercancía | Adaptada; pendiente de revisión |
| 10 | Historial de compras (`PurchaseHistoryPage`) | Historial de compras | Adaptada; pendiente de revisión |
| 11 | Historial de movimientos (`InventoryMovementHistoryPage`) | Movimientos de inventario | Adaptada; pendiente de revisión |
| 12 | Salida de inventario (`OtherExitPage`) | Salidas / mermas | Adaptada; pendiente de revisión |
| 13 | Clientes y crédito (`CustomerManagementPage`) | Clientes | Adaptada; pendiente de revisión |
| 14 | Pagos (`PaymentManagementPage`) | Cobro y pagos | Adaptada; pendiente de revisión |
| 15 | Reportes (`ReportsPage`) | Tarjetas y jerarquía visual de la referencia compartida | Adaptada; pendiente de revisión en Windows y Android |
| 16 | Gestión de cuentas de cajero (`CashierSetupPage`) | Usuarios / administración | Rediseñada; pendiente de revisión |
| 17 | Sincronización y conexión (`SyncManagementPage`) | Panel de sincronización | Rediseñada; pendiente de revisión |
| 18 | Revisión de conflictos (`ConflictReviewPage`) | Sincronización | Rediseñada; pendiente de revisión |
| 19 | Vinculación de dispositivo (`DevicePairingPage`) | Sincronización | Rediseñada; pendiente de revisión |
| 20 | Lector QR de vinculación (`PairingQrScannerPage`) | Sincronización | Rediseñada; pendiente de revisión |
| 21 | Más opciones (`MoreOptionsPage`) | Vista del botón «Más» | Adaptada; pendiente de revisión |
| 22 | Revisión de venta (`SaleReviewPage`) | Referencia de confirmación | Adaptada; reemplaza visualmente la confirmación nativa existente; pendiente de revisión |

## Notas

- El rediseño de ventas presenta la selección de productos como catálogo, mantiene la cantidad manual, las acciones de agregar y quitar, contado/crédito y selección de cliente. El carrito se muestra a la derecha en escritorio y desplegable en móvil; la revisión mantiene el mismo borrador, confirmación, cancelación y reintento. El comprobante conserva todos sus datos. La referencia orienta únicamente la apariencia; no incorpora caja, turnos, filtros nuevos, escáner, impuestos, descuentos, cobros, impresión ni compartir.
- `ReportsPage` conserva las consultas de ventas, inventario y movimientos, sus filtros y todos los campos de resultados. Se adaptaron las pestañas, tarjetas, colores y disposición de filtros para escritorio y Android; la imagen compartida se usa como referencia visual.
- La página `moviles` de Figma incluye una barra inferior persistente con Inicio, Ventas, Inventario, Reportes y Más. Se añadió en Android a las vistas de operación; Más abre una pantalla dedicada que agrupa todas las opciones disponibles. En escritorio se mantiene la navegación lateral y se añadieron iconos a sus accesos.
- `MoreOptionsPage` adapta la pantalla del botón «Más» con las opciones que ya existen en la aplicación. Proveedores, caja, configuración y ayuda aparecen en la referencia Figma, pero no se agregaron porque todavía no son funciones implementadas.
- `StockConsultationPage` se registra por separado del catálogo porque es una pantalla independiente en la aplicación.
- Para `InventoryMovementHistoryPage` se revisó el diseño de escritorio `movimientos de invetario` en Figma; no hay una vista específica equivalente en `moviles`. Se adaptó el listado de datos existentes y se omitieron indicadores, filtros y exportación que la aplicación aún no ofrece.
- Para `OtherExitPage` se revisó en `interfaces` el formulario modal `agregar una nueva salida`; no hay un diseño equivalente en `moviles`. Se adaptó el formulario actual y se mantuvieron solo los motivos, campos y confirmación que ya están implementados.
- Para `CustomerManagementPage` se buscaron referencias de clientes y crédito en ambas páginas de Figma; no hay una pantalla dedicada. Las menciones de cliente aparecen dentro de ventas y compras, por lo que se aplicaron los estilos visuales generales sin agregar filtros ni acciones.
- Para `PaymentManagementPage` se buscaron pagos y saldos en `interfaces` y `moviles`. Figma contiene elementos de cobro en ventas y pagos en movimientos de caja, pero no una pantalla dedicada para registrar abonos a deudas de clientes. Se aplicó el estilo visual general y se conservaron las funciones actuales de selección de cuenta, confirmación, registro e historial.
- No encontré una pantalla móvil dedicada al catálogo en la página `moviles` de Figma; esa vista se adapta al teléfono con el mismo contenido disponible y una disposición de una columna.
- Los diseños muestran campos, cifras, filtros y acciones que algunas vistas actuales no tienen. No se agregan a la aplicación durante esta adaptación visual.
- `CoordinatorRefreshPage` es una clase base, no una pantalla visible, por lo que no se marca como vista.
