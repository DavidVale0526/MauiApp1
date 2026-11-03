# TASK — Completar facturación para el alcance de la tesis

**Estado general:** Etapas 1, 2 y 3 implementadas en código; migración central y validación funcional integral pendientes de Etapa 4
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
- [x] Mantener la información y reglas del comprobante iguales en Windows y Android; adaptar solo la presentación y las acciones disponibles por plataforma.
- [ ] No añadir impuestos, descuentos, precios editables durante la venta, facturación electrónica ni integración fiscal en estas etapas.
- [x] Crear migración compatible con los datos actuales; aplicar el esquema a la base central instalada queda pendiente en Etapa 4.
- [ ] No marcar etapas como completas hasta satisfacer su criterio de aceptación y dejar evidencia.

## Etapa 0 — Confirmar contratos y reglas antes de programar

- [x] Revisar `SaleDraft`, `SaleRecordedPayload`, `SaleEntity`, `SaleLineEntity`, `SaleHistoryEntry`, `EfSaleService` y los appliers de sincronización.
- [x] Revisar el modelo de cuenta por cobrar y confirmar que `PaymentEntity` representa abonos, no cobros iniciales de contado.
- [x] Definir los métodos de pago internos incluidos: efectivo, tarjeta y transferencia; permitir uno o varios métodos en una venta solo si el total queda liquidado.
- [x] Definir que una venta de contado debe quedar pagada por completo; el efectivo entregado puede ser mayor o igual al importe asignado a efectivo y el cambio se calcula sin tratarlo como ingreso.
- [x] Definir que las ventas a crédito conservan su comportamiento actual y que los pagos iniciales parciales/créditos mixtos no se habilitan sin modelar correctamente el saldo inicial.
- [x] Definir el formato de un folio interno legible y estable: usar el identificador completo de operación (`V-` + GUID), sin presentarlo como numeración tributaria ni depender de una secuencia central para registrar ventas offline.
- [x] Definir política de anulaciones y devoluciones: solo Administrador; anulación completa una vez y sin devoluciones previas; devolución por renglón hasta la cantidad vendida; motivo obligatorio.
- [x] Definir el tratamiento monetario: contado registra el método declarado del reembolso; crédito reduce deuda abierta y bloquea excedentes tras abonos, sin saldo negativo ni reembolso implícito.
- [x] Definir concurrencia: el coordinador valida el acumulado y rechaza el exceso como conflicto.
- [x] Registrar decisiones antes de implementar la Etapa 3.

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

- [x] Mostrar identificador/folio interno, fecha y hora local, negocio, usuario que atendió, renglones, cantidad y unidad, precio unitario, total por renglón y total general.
- [x] Mostrar método(s) de pago, importe asignado por método, efectivo recibido y cambio cuando corresponda.
- [x] Para ventas a crédito, mostrar cliente, saldo pendiente y vencimiento, sin presentar el saldo como pago recibido.
- [x] Incluir de forma visible “Comprobante interno — no es factura ni comprobante fiscal”.
- [x] No incluir impuestos calculados ni campos fiscales ficticios.
- [x] Usar los datos históricos guardados en la venta; no tomar nombres, precios o unidades actuales del catálogo para rehacer comprobantes anteriores.

### Acciones

- [x] Generar PDF localmente sin requerir Internet ni servicios externos.
- [x] Permitir abrir/guardar/compartir el PDF mediante los mecanismos nativos disponibles en Windows y Android.
- [x] Permitir imprimir desde el lector de PDF abierto y comunicarlo claramente; no se añadió un flujo de impresión directa específico de plataforma.
- [x] Permitir volver a generar/compartir el comprobante desde el historial, sin crear una nueva venta.
- [x] Gestionar almacenamiento temporal y errores de permisos/cancelación sin perder ni alterar la venta.
- [x] Asegurar que el identificador o folio que aparece en el comprobante permita localizar exactamente la venta original.

**Estado:** implementación terminada y Windows/Android compilan sin errores. La generación, visualización real del PDF, selectores nativos y el flujo de compartir/imprimir deben comprobarse manualmente en los dispositivos y se registrarán en Etapa 4.

**Criterio de aceptación:** el usuario genera desde una venta confirmada un comprobante PDF legible, con datos de pago correctos, que puede abrirse/compartirse en ambas plataformas; el historial permite repetir la acción y el documento mantiene la advertencia de no fiscal.

## Etapa 3 — Anulación y devolución con trazabilidad

### Diseño de las operaciones

**Reglas adoptadas para implementar esta versión:** solo Administrador puede anular o devolver; el motivo es obligatorio; el importe se calcula con precios y cantidades capturados en la venta. Cada línea devuelta indicará si vuelve a inventario vendible. En contado, el reembolso se registra como una salida explícita vinculada a la reversión, con método y responsable; representa la declaración del Administrador, no sustituye un módulo de caja. En crédito, primero se reduce el saldo abierto. Si el valor a devolver supera la deuda abierta por abonos previos, se bloquea hasta una conciliación explícita; no se crea crédito de cliente ni saldo negativo implícito. El coordinador valida referencias y acumulados concurrentes; rechazos deben quedar visibles como conflicto y nunca como aplicados.

- [x] Añadir operaciones independientes para anular una venta completa y registrar devoluciones parciales o completas por producto.
- [x] Guardar identificador, venta original, responsable, fecha UTC, motivo obligatorio, líneas/cantidades e importe asociado.
- [x] Mantener intactos encabezado y renglones originales; el historial muestra el estado derivado y las operaciones relacionadas.
- [x] Definir permisos: solo Administrador puede anular o devolver; Cajero no tiene esa facultad en esta versión.
- [x] Impedir anulaciones repetidas y devoluciones acumuladas superiores a las cantidades vendidas en cliente y coordinador.
- [x] Definir el tratamiento conservador de crédito: reducir deuda abierta primero y bloquear una devolución que exceda el saldo tras abonos; no crear crédito de cliente implícito.
- [x] Definir que el reembolso contado se registra como salida explícita vinculada a la reversión, con método y responsable; el registro documenta la declaración del Administrador y no reemplaza una caja que el sistema no administra.

### Inventario, cuentas y consistencia

- [x] Para producto devuelto vendible, crear movimiento de entrada vinculado a la reversión; nunca reescribir el movimiento original.
- [x] Para producto no vendible, guardar motivo y disposición en la auditoría. No crear otra salida porque la venta ya descontó esa cantidad.
- [x] Aplicar reversión, inventario y cuenta por cobrar en transacción local; usar idempotencia y validación central para reintentos/concurrencia.
- [x] En crédito, reducir deuda de forma validada; no permitir saldo negativo ni borrar abonos.
- [x] Extender sincronización: coordinador valida referencias, acumulados, importes y permisos; conflicto recibe resumen específico.
- [x] Exponer “Anulada”, “Parcialmente devuelta” o “Devuelta”, con motivo, responsable e importe.

### Interfaz y comprobantes

- [x] Añadir acciones para anular o devolver desde el historial, solo para Administrador.
- [x] Exigir cantidades y motivo; decidir por renglón si vuelve al inventario y pedir razón si se dispone.
- [x] Mostrar el efecto monetario antes de confirmar; registrar la decisión de inventario por renglón.
- [x] Emitir PDF interno por reversión, enlazado a la venta y disponible para guardar, compartir o abrir/imprimir.
- [x] Adaptar la interfaz a Windows y Android y bloquear acciones repetidas durante el registro.

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
- [x] Compilar Windows, Android y Coordinador; las tres compilaciones finalizaron sin errores.
- [x] Revisar que EF Core detecte `20261103212753_SaleReversals` como migración pendiente.
- [ ] Aplicar la migración con el actualizador de la base central y comprobar que conserva los datos existentes.
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
- **Etapa 0 completada:** contratos, límites financieros, folio interno y reglas de reversión definidos e implementados.
- **3 de noviembre de 2026 — Etapa 2 implementada:** comprobante PDF local con datos históricos, folio interno completo, detalle de pagos/cambio y saldo de crédito; acciones nativas para guardar, compartir y abrir/imprimir desde Windows y Android; acceso disponible al confirmar una venta y desde su historial. Ambas plataformas compilaron sin errores. Falta comprobar en dispositivos que el PDF se renderice correctamente y que funcionen los selectores y destinos nativos; esas verificaciones quedan en Etapa 4.
- **3 de noviembre de 2026 — Etapa 3 implementada en código:** anulaciones/devoluciones append-only, validación de cantidades e importes históricos, reembolso declarado en contado y reducción segura de deuda en crédito; restitución de stock solo para productos vendibles y motivo para productos no vendibles; sincronización validada por coordinador e idempotencia; historial y constancias PDF individuales. Windows, Android y Coordinador compilaron sin errores. Falta aplicar la migración en la base central instalada y ejecutar pruebas funcionales/LAN de Etapa 4.

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
