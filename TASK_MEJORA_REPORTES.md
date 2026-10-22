# TASK — Mejora paso a paso del módulo de reportes

**Estado general:** En progreso — etapas 0 y 1 iniciadas  
**Creado:** 21 de octubre de 2026  
**Auditoría de referencia:** [PLAN_MEJORA_REPORTES.md](PLAN_MEJORA_REPORTES.md)

## Objetivo

Corregir los problemas de interacción y presentación detectados en Reportes para Windows y Android, y reforzar la confiabilidad de sus consultas sin cambiar el alcance funcional aprobado.

## Reglas de alcance

- [ ] Mantener las secciones Ventas, Inventario y Movimientos.
- [ ] Mantener los filtros actuales y la fuente SQLite local.
- [ ] Mantener el aviso de datos locales que podrían no estar sincronizados.
- [ ] No añadir ganancias, márgenes, costos, cobros, rankings, gráficos ni exportación.
- [ ] Implementar una etapa a la vez y marcarla completa solo después de revisar su criterio de aceptación.

## Etapa 0 — Reproducir y fijar el comportamiento actual

- [x] Registrar la evidencia reportada en Windows: los campos muestran fechas, pero el usuario no logra abrir o cambiar el calendario.
- [x] Inspeccionar los cuatro `DatePicker` y confirmar que están dentro de encabezados de `CollectionView` y contenedores visuales.
- [x] Abrir Reportes en Windows y probar «Desde» y «Hasta» en Ventas.
- [x] Abrir Reportes en Windows y probar «Desde» y «Hasta» en Movimientos.
- [ ] Repetir las cuatro interacciones en Android.
- [ ] Registrar si el selector no abre, abre fuera de pantalla, no permite cambiar fecha o no actualiza el valor mostrado.
- [ ] Consultar un periodo sin registros y confirmar si aparece «Sin resultados».
- [ ] Confirmar qué valores muestran Modalidad, Cliente, Categoría y Estado al cargar.
- [ ] Probar qué ocurre al salir y volver a Reportes después de seleccionar filtros.
- [ ] Probar cambio de sección mientras una consulta está cargando.

**Cierre de etapa:** parcial. La falla se reprodujo y corrigió en la aplicación Windows; los cuatro campos ya se comprobaron en ejecución. El usuario confirmó selección y filtrado en Android. Queda el recorrido completo de los demás casos de esta etapa.

## Etapa 1 — Corregir los selectores de fecha

- [x] Revisar el `DatePicker` dentro del `CollectionView.Header` y su interacción con el contenedor visual.
- [x] Separar los filtros del `CollectionView.Header` en Windows: en ejecución, sus controles no respondían al clic mientras los botones de sección sí lo hacían.
- [x] Usar un botón visible con la fecha y el icono de calendario para que el campo reciba directamente la interacción.
- [x] Abrir el selector de fecha nativo de Windows o Android desde ese botón, sin depender de un `DatePicker` oculto.
- [x] Usar el mismo comportamiento para fechas de ventas y movimientos.
- [x] Configurar la actualización bidireccional de `DateFrom` y `DateTo`.
- [x] Mantener visibles el valor, la etiqueta y una indicación de calendario accesible.
- [x] Comprobar que el calendario de Windows se muestre completo tanto en ventana normal como maximizada.
- [x] Conservar el botón «Consultar» como acción para aplicar los filtros seleccionados.
- [x] Mostrar el periodo consultado después de completar la consulta.

**Fallo reproducido:** cambiar el control de fecha no resolvía el bloqueo. En la compilación actual de Windows, los campos y desplegables dentro del encabezado de `CollectionView` no respondían al ratón. Al mover esos filtros a un contenedor independiente, los cuatro calendarios y el desplegable de tipos de movimiento respondieron. La prueba incluyó listas sin resultados.

**Implementación actual:** en Windows, los filtros de cada sección se alojan fuera de la lista de resultados y pueden desplazarse cuando la ventana es baja. Cada fecha abre un `ContentDialog` con `CalendarView`, alojado en la ventana de la página; Aceptar actualiza la fecha y Cancelar la conserva. Se impide abrir dos diálogos simultáneos y los errores se registran en la salida de depuración. La referencia del icono se corrigió a `calendar.png`, que es el recurso generado por MAUI. En Android se conserva el encabezado desplazable y su `DatePickerDialog`.

**Validación en ejecución — Windows:** realizada sobre `MauiApp1/bin/Debug/net10.0-windows10.0.19041.0/win-x64/MauiApp1.exe`, comprobando la ruta del proceso. Los cuatro selectores abrieron; Ventas permitió elegir Desde 20/10/2026 y Hasta 22/10/2026, y «Consultar ventas» mostró ese periodo aplicado. En Movimientos, seleccionar 19/10 y cancelar mantuvo 20/10; aceptar Hasta 21/10 actualizó el campo y permitió ejecutar la consulta. También se abrió el desplegable de tipos y se comprobó la sección Inventario con filas visibles. No se crearon ni modificaron operaciones de negocio durante estas pruebas.

**Android:** selección y filtrado confirmados por el usuario; compilación repetida después del ajuste exclusivo de la distribución de Windows. Queda el recorrido manual de los cuatro campos Android como comprobación adicional.

### Evidencia de compilación — 21 de octubre de 2026

- Windows (`net10.0-windows10.0.19041.0`): compilación correcta después de separar los filtros de la lista, 0 advertencias y 0 errores; validación interactiva descrita arriba.
- Android (`net10.0-android`): compilación correcta, 0 advertencias y 0 errores; el usuario ya confirmó la selección y el filtrado en el dispositivo.

**Criterio de aceptación:** en Windows y Android se abre el selector al tocar el campo; la fecha elegida se refleja en la pantalla y se utiliza al consultar ventas o movimientos.

## Etapa 2 — Corregir filtros iniciales y conservar selección

- [ ] Mostrar los valores iniciales esperados («Todas/Todos») en los selectores, no sus títulos de campo.
- [ ] Al recargar clientes, categorías o productos, conservar la selección existente si todavía está disponible.
- [ ] Si el elemento seleccionado dejó de existir, volver al valor «Todos/Todas» de forma visible.
- [ ] Conservar filtros al volver a entrar a Reportes.
- [ ] Conservar filtros al recibir cambios locales o sincronizados.
- [ ] Confirmar que cada selector filtre los resultados por el identificador correcto.

**Criterio de aceptación:** los filtros seleccionados no cambian silenciosamente al recargar la vista, recibir datos o cambiar entre secciones.

## Etapa 3 — Separar carga, resultados, vacío y error

- [ ] Mostrar un estado de carga comprensible mientras se ejecuta una consulta.
- [ ] Mostrar el estado «Sin resultados» únicamente cuando la consulta termina correctamente con cero registros.
- [ ] Hacer visible el estado vacío debajo del encabezado en Windows y Android.
- [ ] Mostrar errores por separado del estado vacío.
- [ ] Conservar los últimos resultados correctos si una nueva consulta falla, o indicar claramente que la información visible está desactualizada.
- [ ] Evitar que un fallo de carga de catálogos se presente como filtros válidos sin opciones.
- [ ] Agregar registro de diagnóstico para fallos técnicos, manteniendo mensajes sencillos en pantalla.

**Criterio de aceptación:** la pantalla siempre explica si está cargando, si encontró datos, si no hay datos o si ocurrió un error.

## Etapa 4 — Evitar consultas descartadas y resultados obsoletos

- [ ] Revisar la interacción entre `ChangeSectionAsync()` e `IsBusy`.
- [ ] Coordinar los cambios de sección con la consulta actual: cancelar la anterior o ejecutar la última sección solicitada al terminar.
- [ ] Evitar que dos consultas simultáneas escriban resultados fuera de orden.
- [ ] Bloquear temporalmente los controles que no puedan usarse durante una consulta, o mantenerlos funcionales con cancelación controlada.
- [ ] Confirmar que los resultados visibles correspondan a la sección y filtros aplicados.

**Criterio de aceptación:** cambiar de sección o actualizar filtros durante una carga no deja una vista vacía ni presenta resultados de otra consulta.

## Etapa 5 — Reforzar exactitud de fechas y datos

- [ ] Confirmar que el periodo local incluya todo el día final y excluya el día siguiente.
- [ ] Revisar fecha inicial igual a fecha final, periodo invertido, fin de mes, cambio de año y año bisiesto.
- [ ] Mantener límites UTC inclusivo/exclusivo en el servicio de reportes.
- [ ] Definir cómo se garantiza una zona horaria común del negocio si los dispositivos tienen zonas diferentes.
- [ ] Revisar las uniones de ventas y movimientos con usuarios y de inventario con categorías.
- [ ] Evitar que la falta de una relación local o sincronizada oculte un registro sin explicación; usar valores de respaldo o informar la anomalía.
- [ ] Confirmar que las filas de movimientos conserven nombre y unidad históricos.

**Criterio de aceptación:** los límites del periodo no omiten ni duplican registros y los datos históricos siguen visibles ante relaciones incompletas.

## Etapa 6 — Rendimiento, accesibilidad y revisión visual

- [ ] Medir el comportamiento con un historial amplio de ventas y movimientos.
- [ ] Si el volumen lo requiere, implementar carga incremental o paginación sin cambiar qué registros pueden consultarse.
- [ ] Revisar la distribución de filtros y tarjetas al redimensionar una ventana Windows.
- [ ] Revisar scroll, barra inferior y controles táctiles en Android.
- [ ] Revisar navegación con teclado, foco visible, etiquetas accesibles, contraste y áreas táctiles.
- [ ] Completar un recorrido de Ventas, Inventario y Movimientos en ambos sistemas operativos.
- [ ] Actualizar esta lista y la auditoría si el alcance aprobado cambia.

**Criterio de aceptación:** las consultas siguen respondiendo con historiales amplios y los controles se pueden operar con ratón, teclado o tacto según el dispositivo.

## Orden de implementación

1. Etapa 0: reproducir.
2. Etapa 1: selector de fechas.
3. Etapa 2: filtros.
4. Etapa 3: estados de pantalla.
5. Etapa 4: concurrencia y resultados actuales.
6. Etapa 5: exactitud e integridad.
7. Etapa 6: rendimiento y revisión multiplataforma.

## Archivos principales a revisar

- `MauiApp1/Presentation/Views/ReportsPage.xaml`
- `MauiApp1/Presentation/Views/ReportsPage.xaml.cs`
- `MauiApp1/Presentation/ViewModels/ReportsViewModel.cs`
- `MauiApp1/Presentation/Views/CoordinatorRefreshPage.cs`
- `MauiApp1.Infrastructure/Reports/EfReportService.cs`
- `MauiApp1.Application/Reports/IReportService.cs`
- `ARCHITECTURE.md`
