# TASK — Completar facturación para el alcance de la tesis

**Estado general:** Etapa 1 implementada; comprobaciones manuales integrales pendientes de Etapa 4  
**Fecha de creación:** 3 de noviembre de 2026  
**Plataformas:** Windows y Android; sincronización LAN existente  
**Alcance:** captura de pagos de contado y cambio, comprobante imprimible/compartible, anulaciones y devoluciones con trazabilidad

## Objetivo

Completar el flujo de facturación interno de Miscelánea Ari para que registre cómo se liquidó una venta de contado, entregue una constancia que se pueda imprimir o compartir y permita corregir ventas mediante anulaciones y devoluciones auditables. Mantener el funcionamiento sin Internet, las ventas a crédito existentes, el inventario y la sincronización LAN.

El comprobante seguirá siendo **interno y no fiscal**. Esta tarea no incorpora DGI, Hacienda, numeración fiscal, firma electrónica, envío de documentos tributarios ni cálculo legal de impuestos.

## Justificación breve para la tesis

En esta etapa se prioriza una facturación operativa interna: registrar el pago recibido, facilitar la entrega de una constancia y mantener trazabilidad cuando una venta deba anularse o devolverse. Se plantea así porque el sistema es un proyecto académico de inventario y ventas que funciona localmente y sincroniza por LAN; integrar requisitos de facturación electrónica, validación tributaria y conexión con DGI/Hacienda ampliaría considerablemente el alcance y dependería de trámites, certificaciones e infraestructura externa. Por ello, el comprobante se identifica explícitamente como interno y no fiscal, dejando la integración tributaria como trabajo futuro.

## Estado funcional previo a esta mejora

- El POS permite registrar varios productos por venta, cantidades fraccionarias hasta tres decimales, revisión previa y confirmación.
- Se guarda una copia de los datos del producto y del precio usados en la venta; se descuenta el inventario y se previenen duplicados ante reintentos.
- Se registran ventas de contado y ventas a crédito. Las ventas a crédito requieren cliente, crean una cuenta por cobrar y admiten abonos.
- El historial y el comprobante interno se pueden consultar dentro de la aplicación.
- El comprobante actual indica que no es fiscal y que no guarda datos del cobro.
- El modelo actual no registra para una venta de contado el método de pago, efectivo recibido ni cambio. La entidad `Payments` actual corresponde a abonos de cuentas por cobrar; no debe reutilizarse para los pagos iniciales de ventas.
- No hay un flujo de anulación o devolución que conserve la venta original y deje su reversión auditada.

## Reglas de alcance y preservación

- [ ] Mantener el flujo existente de catálogo → carrito → revisión → confirmación.
- [ ] Mantener cantidades fraccionarias, precios capturados como instantánea, validaciones de inventario, cuentas por cobrar y abonos.
- [ ] Mantener soporte offline y transmitir las nuevas operaciones mediante el outbox y el coordinador LAN existentes.
- [ ] No borrar ni editar destructivamente una venta ya confirmada; toda anulación/devolución debe quedar como operación nueva enlazada a la original.
- [ ] Separar el pago inicial de una venta de los abonos posteriores a una deuda.
- [x] Usar importes enteros en unidades monetarias menores (céntimos); la captura limita el dinero a dos decimales.
- [ ] Mantener la información y reglas del comprobante iguales en Windows y Android; adaptar solo la presentación y las acciones disponibles por plataforma.
- [ ] No añadir impuestos, descuentos, precios editables durante la venta, facturación electrónica ni integración fiscal en estas etapas.
- [ ] Actualizar migraciones y despliegue del coordinador sin reiniciar ni reemplazar los datos comerciales existentes.
- [ ] No marcar etapas como completas hasta satisfacer su criterio de aceptación y dejar evidencia.

## Etapa 0 — Confirmar contratos y reglas antes de programar

- [x] Revisar `SaleDraft`, `SaleRecordedPayload`, `SaleEntity`, `SaleLineEntity`, `SaleHistoryEntry`, `EfSaleService` y los appliers de sincronización.
- [x] Revisar el modelo de cuenta por cobrar y confirmar que `PaymentEntity` representa abonos, no cobros iniciales de contado.
- [x] Definir los métodos de pago internos incluidos: efectivo, tarjeta y transferencia; permitir uno o varios métodos en una venta solo si el total queda liquidado.
- [x] Definir que una venta de contado debe quedar pagada por completo; el efectivo entregado puede ser mayor o igual al importe asignado a efectivo y el cambio se calcula sin tratarlo como ingreso.
- [x] Definir que las ventas a crédito conservan su comportamiento actual y que los pagos iniciales parciales/créditos mixtos no se habilitan sin modelar correctamente el saldo inicial.
- [ ] Definir el formato de un folio interno legible y estable sin presentarlo como numeración tributaria. Evitar depender de una secuencia central para registrar ventas offline.
- [ ] Definir política de anulaciones y devoluciones: ventana/permiso, ventas que pueden anularse, devolución parcial por renglón, cantidades máximas y relación con pagos o deudas ya registrados.
- [ ] Definir el tratamiento monetario de una devolución cuando la venta fue pagada, quedó a crédito o ya recibió abonos; no crear saldo negativo ni un reembolso ficticio.
- [ ] Definir cómo se resuelven devoluciones concurrentes de la misma venta desde dispositivos diferentes: el coordinador valida el acumulado y rechaza el exceso como conflicto visible.
- [ ] Registrar decisiones y ejemplos de aceptación en esta tarea antes de iniciar la etapa 1.

**Criterio de aceptación:** las reglas de liquidación, cambio, folios, anulación y reembolso están acordadas y no contradicen el flujo actual ni el trabajo offline.

## Etapa 1 — Capturar pagos de contado y calcular cambio

### Modelo y persistencia

- [x] Añadir un modelo explícito para el detalle de liquidación de una venta, separado de los abonos de crédito.
- [x] Guardar por cada método el método, el importe aplicado a la venta y, para efectivo, el efectivo recibido y el cambio calculado.
- [x] Validar los importes por método y que la suma aplicada coincida exactamente con el total de contado.
- [x] Validar que el efectivo recibido cubra el importe asignado; calcular cambio como recibido menos esa parte.
- [x] Registrar métodos, importes y cambio en `Sale.Recorded`, historial y base central.
- [x] Crear migración compatible con bases existentes; no inventar datos para ventas históricas, que se identifican como “sin detalle de pago registrado”.
- [x] Mantener el uso de `PaymentEntity` para abonos posteriores a una cuenta por cobrar.

### Interfaz y reglas del flujo

- [x] En la revisión previa a confirmar, mostrar total, método(s), importe por método, efectivo recibido y cambio.
- [x] Deshabilitar confirmar hasta que se cubra el total y el efectivo recibido sea suficiente.
- [x] Mostrar errores para importes inválidos, insuficientes o con más de dos decimales.
- [x] No modificar precio ni cantidad desde el formulario de pago.
- [x] Mostrar el resumen antes de registrar y conservar el mismo intento e identificadores al reintentar.
- [x] Adaptar la interacción a Android y Windows; ambos destinos compilan.

### Sincronización y datos iniciales

- [x] Extender el applier del coordinador para aplicar el pago de la venta con la venta y sus líneas.
- [x] Hacer la aplicación idempotente: reenvíos no duplican ventas ni pagos.
- [x] Extender la importación inicial para preservar detalles nuevos y mantener registros antiguos sin datos de pago.
- [x] Rechazar payloads con pagos que no coincidan con total, venta o métodos permitidos.

**Estado:** implementación terminada. Windows, Android y Coordinador compilaron; EF reconoce la migración y genera el script SQL esperado. La prueba funcional en dispositivos y la sincronización real se ejecutarán y registrarán en Etapa 4.

**Criterio de aceptación:** una venta pagada en efectivo registra importe y cambio correctos; una venta con método no efectivo no inventa efectivo recibido; ventas a crédito y abonos conservan su funcionamiento; el registro se sincroniza sin duplicarse. La verificación manual queda pendiente en Etapa 4.

## Etapa 2 — Comprobante imprimible y compartible

### Contenido del documento

- [ ] Mostrar identificador/folio interno, fecha y hora local, negocio, usuario que atendió, renglones, cantidad y unidad, precio unitario, total por renglón y total general.
- [ ] Mostrar método(s) de pago, importe asignado por método, efectivo recibido y cambio cuando corresponda.
- [ ] Para ventas a crédito, mostrar cliente, saldo pendiente y vencimiento, sin presentar el saldo como pago recibido.
- [ ] Incluir de forma visible “Comprobante interno — no es factura ni comprobante fiscal”.
- [ ] No incluir impuestos calculados ni campos fiscales ficticios.
- [ ] Usar los datos históricos guardados en la venta; no tomar nombres, precios o unidades actuales del catálogo para rehacer comprobantes anteriores.

### Acciones

- [ ] Generar PDF localmente sin requerir Internet ni servicios externos.
- [ ] Permitir abrir/guardar/compartir el PDF mediante los mecanismos nativos disponibles en Windows y Android.
- [ ] Añadir acción de imprimir si la plataforma dispone de un flujo soportado; si no, permitir imprimir desde el PDF abierto y comunicarlo claramente.
- [ ] Permitir volver a generar/compartir el comprobante desde el historial, sin crear una nueva venta.
- [ ] Gestionar almacenamiento temporal y errores de permisos/cancelación sin perder ni alterar la venta.
- [ ] Asegurar que el identificador o folio que aparece en el comprobante permita localizar exactamente la venta original.

**Criterio de aceptación:** el usuario genera desde una venta confirmada un comprobante PDF legible, con datos de pago correctos, que puede abrirse/compartirse en ambas plataformas; el historial permite repetir la acción y el documento mantiene la advertencia de no fiscal.

## Etapa 3 — Anulación y devolución con trazabilidad

### Diseño de las operaciones

- [ ] Añadir operaciones de dominio independientes para anular una venta completa y registrar devoluciones parciales o completas de productos.
- [ ] Guardar identificador de la reversión, venta original, responsable, fecha UTC, motivo obligatorio, líneas/cantidades afectadas e importe asociado.
- [ ] Mantener intactos el encabezado y los renglones originales de la venta; el historial debe mostrar su estado derivado y las operaciones de reversión relacionadas.
- [ ] Definir permisos: solo roles autorizados pueden anular o devolver; un cajero solo podrá hacerlo si la política del negocio lo autoriza expresamente.
- [ ] Impedir anular dos veces una misma venta y devolver cantidades acumuladas superiores a las vendidas.
- [ ] Bloquear o conciliar devoluciones si la cuenta tiene abonos: reducir la deuda abierta primero y registrar cualquier importe reembolsado o crédito del cliente mediante un movimiento explícito.
- [ ] No permitir registrar salida de efectivo como reembolso si no existe una transacción de reembolso confirmada.

### Inventario, cuentas y consistencia

- [ ] Para producto devuelto que vuelve a estar disponible, crear un movimiento de entrada vinculado a la reversión; nunca reescribir el movimiento original.
- [ ] Si el producto no vuelve a inventario vendible, guardar el motivo y registrar la baja/merma por la ruta de inventario correspondiente.
- [ ] Aplicar reversión de venta, ajuste de inventario, pagos y cuenta por cobrar en una transacción coherente o mediante operaciones idempotentes con recuperación definida.
- [ ] En crédito, recalcular saldo pendiente de forma validada; no permitir saldo negativo ni borrar abonos ya efectuados.
- [ ] Extender sincronización y resolución de conflictos para reversiones; el coordinador valida referencias, acumulados y permisos antes de aplicarlas.
- [ ] Exponer en historial el estado “Anulada”, “Parcialmente devuelta” o “Devuelta”, con vínculo al detalle de auditoría.

### Interfaz y comprobantes

- [ ] Añadir acciones claramente diferenciadas para anular y devolver desde el detalle/historial de una venta.
- [ ] Exigir selección de líneas/cantidades para devolución parcial y motivo antes de confirmar.
- [ ] Mostrar un resumen de efecto sobre inventario, total, saldo pendiente y reembolso antes de confirmar.
- [ ] Emitir una constancia interna de anulación/devolución vinculada al comprobante original.
- [ ] Adaptar el flujo a Windows y Android y evitar acciones duplicadas durante operaciones en curso.

**Criterio de aceptación:** una devolución parcial o anulación deja evidencia inalterable, ajusta inventario y saldo sin duplicarse, se propaga por LAN y no puede superar cantidades o importes disponibles; fallos de sincronización se muestran como pendientes/conflictos sin afirmar que se aplicaron en el coordinador.

## Etapa 4 — Pruebas y revisión integral

- [ ] Probar efectivo exacto, efectivo con cambio, pago con tarjeta/transferencia y combinación de métodos; verificar redondeo y total exacto.
- [ ] Probar importes insuficientes, vacíos, negativos, duplicados y valores fuera de rango.
- [ ] Probar ventas de contado, crédito, abonos existentes e historial anterior a la migración.
- [ ] Probar compartir, guardar, volver a abrir e imprimir el PDF en Windows y Android, incluida cancelación del diálogo de compartir.
- [ ] Probar devolución parcial, devolución completa, anulación completa, motivo vacío, intento repetido y cantidad superior a la vendida.
- [ ] Probar venta a crédito con y sin abonos antes de devolver/anular según las reglas aprobadas.
- [ ] Probar devolución de producto vendible y producto no vendible/merma.
- [ ] Probar pérdida de conexión antes, durante y después de sincronizar; repetir el envío y comprobar que no duplica operaciones.
- [ ] Probar dos dispositivos que intenten devolver el mismo renglón al mismo tiempo y comprobar rechazo seguro del exceso.
- [ ] Revisar permisos de Administrador y Cajero y validar mensajes de éxito, error, pendiente y conflicto.
- [ ] Compilar Windows, Android y Coordinador; revisar migración y preparar el actualizador correspondiente para la base central.
- [ ] Actualizar guía de uso y documentar que el comprobante es interno y no fiscal.

**Criterio de cierre:** se cumplen las pruebas en ambos clientes y coordinador, el esquema central puede actualizarse conservando datos, los reintentos son idempotentes y las ventas originales permanecen auditables tras devoluciones y anulaciones.

## Orden de implementación

1. Etapa 0: acordar reglas financieras y de reversión.
2. Etapa 1: captura de pago, efectivo recibido y cambio; sincronización/migración.
3. Etapa 2: comprobante PDF imprimible/compartible.
4. Etapa 3: anulación/devolución con efecto financiero y de inventario auditable.
5. Etapa 4: pruebas, despliegue del esquema central y documentación.

## Registro de avance

- **3 de noviembre de 2026 — Etapa 1 implementada:** detalle de pagos de contado separado de los abonos de crédito; captura de efectivo/tarjeta/transferencia con cambio; persistencia local y central; importación inicial y reintentos idempotentes; historial identifica ventas antiguas sin detalle. Migración `20261103120000_SalePaymentDetails` agregada. Compilación de Windows, Android y Coordinador completada sin errores. Queda pendiente probar venta en efectivo, cambio, métodos mixtos, crédito y sincronización en dispositivos como parte de Etapa 4.
- **Etapa 0 sigue parcialmente pendiente:** la política de folios internos y reglas financieras de anulación/devolución se resolverán antes de las etapas 2 y 3, respectivamente.

## Archivos principales a revisar

- `MauiApp1.Application/Sales/SaleDraft.cs`
- `MauiApp1.Application/Sales/SaleRecordedPayload.cs`
- `MauiApp1.Application/Sales/SaleHistoryEntry.cs`
- `MauiApp1.Infrastructure/Sales/EfSaleService.cs`
- `MauiApp1.Infrastructure/Credit/EfPaymentService.cs`
- `MauiApp1.Infrastructure/Sync/CoordinatorBusinessOperationApplier.cs`
- `MauiApp1.Infrastructure/Sync/LocalInitialDataImportService.cs`
- `MauiApp1/Presentation/ViewModels/SaleRegistrationViewModel.cs`
- `MauiApp1/Presentation/ViewModels/SaleTicketViewModel.cs`
- `MauiApp1/Presentation/Views/SaleReviewPage.xaml`
- `MauiApp1/Presentation/Views/SaleTicketPage.xaml`
- `MauiApp1/Presentation/Views/SaleHistoryPage.xaml`

Modificar otros archivos solo cuando la etapa activa lo requiera. Mantener separados los abonos de crédito y los pagos de venta, y preservar el protocolo de sincronización vigente.
