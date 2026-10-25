# Tareas: rediseño del panel principal

Alcance: reorganizar y enriquecer el panel con información de solo lectura, conservando las funciones, destinos, permisos y opciones actuales de Miscelánea Ari.

## 1. Inventario y límites

- [x] Revisar los accesos del menú lateral y de las tarjetas actuales.
- [x] Confirmar cuáles accesos del panel no aparecen en el menú lateral y deben conservarse.
- [x] Revisar los datos disponibles para ventas, inventario, productos y usuarios.
- [x] Dejar fuera la métrica de ganancias: no hay costos de compra por producto suficientes para calcularla con precisión.
- [ ] Confirmar en ejecución que cada botón mantiene su destino y permisos.

## 2. Datos de resumen

- [x] Definir un servicio de consulta de solo lectura para el panel.
- [x] Obtener importe y número de ventas del día usando el día local del dispositivo y límites UTC correctos.
- [x] Obtener importe de ventas del mes actual.
- [x] Obtener totales diarios para los últimos siete días.
- [x] Obtener los cinco productos más vendidos del mes, separando unidades distintas.
- [x] Obtener las cinco ventas más recientes con fecha, usuario, modalidad, cliente e importe.
- [x] Conservar la alerta de existencias bajas y su detalle actual.
- [x] Presentar errores o ausencia de datos sin bloquear los accesos operativos.

## 3. Presentación del panel

- [x] Mantener encabezado, usuario y rol actuales.
- [x] Incorporar tarjetas de ventas de hoy, operaciones de hoy, existencias bajas y ventas del mes.
- [x] Incorporar gráfica semanal con los importes reales de los últimos siete días.
- [x] Incorporar lista de productos más vendidos del mes.
- [x] Incorporar sección de últimas ventas, usando únicamente los campos disponibles.
- [x] Mostrar que los importes son ventas registradas, no cobros recibidos ni ganancias.
- [x] Mantener todas las tarjetas y todos sus botones actuales, incluidos pagos, historial de compras, salidas de inventario y creación de cajeros.
- [x] Mantener los accesos laterales actuales y sus controladores de navegación.

## 4. Adaptación de tamaño y plataforma

- [x] Ajustar las columnas de indicadores según el ancho disponible.
- [x] Ajustar la distribución de gráfica y productos más vendidos según el ancho.
- [ ] Evitar desbordamientos y recortes en Windows maximizado y en ventanas reducidas.
- [x] Conservar la navegación inferior y el flujo actual en Android.

## 5. Revisión de cierre

- [ ] Revisar visualmente disponibilidad de datos vacía y errores de carga.
- [x] Recargar el resumen al volver al panel y al recibir cambios locales del coordinador.
- [x] Compilar los destinos Windows y Android sin advertencias ni errores.
- [x] Compilar Windows y Android sin errores.
