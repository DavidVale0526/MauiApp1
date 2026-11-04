# Guía de facturación interna

Esta guía describe las funciones implementadas para registrar ventas y conservar su trazabilidad. El comprobante que genera Miscelánea Ari es **interno; no es una factura ni un comprobante fiscal**.

## Cobrar una venta de contado

1. Abre **Punto de venta** y agrega los productos y cantidades.
2. Revisa el carrito y continúa a la confirmación.
3. Selecciona efectivo, tarjeta o transferencia con los botones rápidos para aplicar todo el total a ese método, o escribe el importe en tarjeta/transferencia: el efectivo aplicado se calcula como el resto del total. La suma aplicada debe coincidir con el total.
4. Para efectivo, escribe cuánto recibió la caja. La pantalla calcula el cambio; confirma solo cuando la venta esté cubierta.
5. Confirma la venta. El detalle de pago queda asociado a esa venta.

Las ventas a crédito mantienen su flujo de cliente, saldo y abonos. Los abonos posteriores no se registran como pago inicial de la venta.

## Abrir, compartir o imprimir un comprobante

Al confirmar la venta puedes generar su comprobante. También puedes volver a abrir la venta desde **Historial de ventas** y generar o compartir el PDF otra vez. Para imprimir, abre el PDF y utiliza la opción de impresión disponible en el dispositivo.

El comprobante muestra el folio interno, los datos de la operación y, si corresponde, los métodos de pago, efectivo recibido y cambio. No contiene folio ni validación tributaria.

## Anular una venta o registrar una devolución

Estas acciones están disponibles únicamente para el Administrador desde el historial de ventas.

- **Anulación:** registra el motivo y confirma la anulación completa. Una venta no se puede anular dos veces ni anular después de registrar devoluciones.
- **Devolución:** indica el motivo, las cantidades devueltas por producto y si cada cantidad vendible vuelve al inventario. El sistema impide exceder las cantidades aún no devueltas.
- **Venta de contado:** declara el método usado para el reembolso. El registro deja constancia de lo declarado; no sustituye un módulo de caja.
- **Venta a crédito:** la devolución reduce primero el saldo pendiente. Si los abonos previos hacen que el reembolso exceda la deuda abierta, el sistema bloquea la operación para evitar un saldo negativo implícito.

La venta original se conserva y la reversión genera su propio registro y comprobante relacionado. Si la operación aún no se ha confirmado en el coordinador, revisa su estado de sincronización antes de asumir que los demás dispositivos ya la recibieron.

## Actualización del coordinador

La base central debe tener aplicada la migración de ventas reversadas antes de usar esta versión con el coordinador instalado. Publicar el coordinador no actualiza por sí mismo el servicio instalado: se debe ejecutar el actualizador de despliegue en Windows y comprobar que el servicio inicia y responde correctamente.

