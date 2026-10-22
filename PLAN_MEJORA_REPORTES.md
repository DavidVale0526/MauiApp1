# Auditoría y plan de mejora del módulo de reportes

**Fecha de revisión:** 21 de octubre de 2026  
**Alcance:** reportes de ventas, inventario y movimientos en Windows y Android.  
**Tipo de revisión:** inspección estática de XAML, ViewModel, consultas SQLite y documentación; no se ejecutó la aplicación durante esta auditoría.

## Resumen

La estructura funcional coincide con la definición existente: ventas por periodo, inventario actual y movimientos por periodo; las consultas son locales y muestran el aviso de datos que podrían no estar sincronizados. El cálculo del periodo usa inicio inclusivo y fin exclusivo del día siguiente, lo cual conserva el día final completo.

La falla confirmada por el usuario es la selección de fechas: los campos muestran fecha e icono, pero el calendario no se abre o no permite cambiarla. La causa exacta aún debe reproducirse en la aplicación. La captura también muestra controles de filtro con texto de título en vez del valor inicial esperado y un área de resultados vacía sin el estado «Sin resultados» visible; deben verificarse en Windows y Android.

## Hallazgos

### RPT-01 — El selector de fecha no permite elegir una fecha

**Prioridad: P0 — bloquea consultas por periodo.**  
**Estado: fallo reportado; causa técnica pendiente de reproducción.**

Los cuatro controles de fecha de ventas y movimientos son `DatePicker` estándar dentro de un `Border`, ubicado dentro del encabezado de un `CollectionView`. No hay un manejador específico que garantice que tocar el campo completo abra el selector en ambas plataformas. En la captura se ve el valor y el icono, pero el usuario informa que no puede abrir o usar el calendario.

**Riesgo:** el usuario no puede consultar periodos históricos ni movimientos por fechas.  
**Referencias:** `MauiApp1/Presentation/Views/ReportsPage.xaml`, fechas de ventas y movimientos; `ReportsPage.xaml.cs`, `ArrangeFilters`.

### RPT-02 — Estado vacío no visible en la captura

**Prioridad: P1.**  
La vista declara un `EmptyView` para las tres colecciones, pero en la captura el área bajo «Resultados de la consulta» queda en blanco. Hay que reproducir la consulta sin registros y confirmar que el estado vacío aparezca después del encabezado, con suficiente espacio y sin quedar recortado por el `CollectionView`.

**Riesgo:** el usuario puede interpretar el área en blanco como carga incompleta o fallo de pantalla.  
**Referencia:** `ReportsPage.xaml`, bloques `CollectionView.EmptyView` de ventas, inventario y movimientos.

### RPT-03 — Los filtros seleccionados no se conservan al recargar

**Prioridad: P1.**  
`LoadAsync()` vuelve a seleccionar la primera opción de cliente, categoría y producto cada vez que carga los catálogos. La página llama este método al aparecer y también al recibir cambios locales del coordinador. Por tanto, volver a Reportes o recibir una actualización puede cambiar silenciosamente un filtro elegido a «Todos/Todas».

**Riesgo:** consultas con criterios distintos a los que el usuario había seleccionado.  
**Referencias:** `ReportsViewModel.cs`, `LoadAsync()`; `ReportsPage.xaml.cs`, `OnAppearing()` y `RefreshAfterCoordinatorChangesAsync()`; `CoordinatorRefreshPage.cs`, `OnLocalDataChanged()`.

### RPT-04 — Valores iniciales de los selectores no coinciden con lo que muestra la captura

**Prioridad: P1 — verificar.**  
El ViewModel inicializa modalidad y cliente con «Todas/Todos», pero la captura muestra «Modalidad» y «Cliente», que son los títulos de los `Picker`. También existe un valor por defecto para categoría y estado. Hay que confirmar que las opciones hayan terminado de cargar y que el valor seleccionado se vea correctamente en Windows y Android.

**Riesgo:** no saber qué filtro se aplicará o creer que el selector está vacío.  
**Referencias:** `ReportsViewModel.cs`, constructor y `LoadAsync()`; `ReportsPage.xaml`, filtros `Picker`.

### RPT-05 — Cambiar de sección durante una consulta puede dejar la nueva sección sin cargar

**Prioridad: P1.**  
`RefreshAsync()` devuelve inmediatamente si `IsBusy` ya es verdadero. `ChangeSectionAsync()` cambia la sección activa antes de llamar a `RefreshAsync()`. Si la consulta anterior sigue ejecutándose, la nueva consulta se descarta y la sección recién abierta puede quedar vacía hasta que el usuario vuelva a pulsar «Consultar».

**Riesgo:** resultados atrasados o una sección vacía pese a existir datos.  
**Referencia:** `ReportsViewModel.cs`, `RefreshAsync()` y `ChangeSectionAsync()`.

### RPT-06 — Error y ausencia de datos pueden confundirse

**Prioridad: P1.**  
Antes de consultar, el ViewModel limpia la colección activa. Si la consulta falla, muestra un error genérico y la colección queda vacía; la pantalla puede presentar a la vez un error y el estado «Sin resultados», o perder los últimos resultados correctos. Los errores no quedan registrados con información suficiente para diagnóstico.

**Riesgo:** el usuario puede confundir una falla de base de datos con una consulta legítimamente vacía.  
**Referencias:** `ReportsViewModel.cs`, `RefreshAsync()`; `ReportsPage.xaml`, etiqueta `ErrorMessage` y `EmptyView`.

### RPT-07 — Las consultas cargan todos los resultados a memoria

**Prioridad: P2 — riesgo de rendimiento al crecer el historial.**  
Ventas y movimientos ejecutan `ToListAsync()` sin límite, paginación ni cancelación. `CollectionView` virtualiza la presentación visual, pero eso no limita la cantidad de registros recuperados de SQLite.

**Riesgo:** consultas lentas y mayor consumo de memoria para periodos largos.  
**Referencia:** `MauiApp1.Infrastructure/Reports/EfReportService.cs`, consultas de ventas y movimientos.

### RPT-08 — Una relación faltante puede ocultar registros históricos

**Prioridad: P2 — protección de integridad.**  
Las consultas de ventas y movimientos usan unión interna con usuarios; inventario usa unión interna con categorías. Si una base local queda incompleta por importación o sincronización, el registro relacionado no aparecerá en el reporte. No se confirmó que existan filas huérfanas actualmente.

**Riesgo:** cifras o productos ausentes sin explicación visible.  
**Referencia:** `EfReportService.cs`, uniones con `UserAccounts` y `Categories`.

## Plan de mejora por etapas

### Etapa 1 — Recuperar la selección de fechas

1. Reproducir el problema en Windows y Android con ventas y movimientos.
2. Asegurar que tocar cualquier parte del campo abra el selector nativo correspondiente; revisar el foco, el área táctil y el comportamiento del `CollectionView.Header`.
3. Mantener una sola implementación reutilizable para los campos «Desde» y «Hasta».
4. Hacer explícita la actualización bidireccional de `DateFrom` y `DateTo` y confirmar que la fecha elegida se refleje inmediatamente.
5. Conservar el botón «Consultar» como acción que aplica los filtros actuales; mostrar con claridad el periodo consultado.

**Criterio de aceptación:** en ambas plataformas se abre el calendario, se puede elegir cualquier fecha válida y las consultas de ventas y movimientos incluyen todo el día final.

### Etapa 2 — Hacer claros los estados y conservar filtros

1. Separar los estados de carga, error, resultados y ausencia real de datos.
2. Reproducir el caso de cero resultados y corregir el `EmptyView` si queda oculto o recortado.
3. Mostrar los valores seleccionados en los filtros y conservar sus identificadores cuando se recarguen las opciones.
4. Evitar borrar resultados correctos antes de saber que la siguiente consulta terminó correctamente.
5. Deshabilitar o coordinar las acciones durante la carga para impedir consultas descartadas al cambiar entre Ventas, Inventario y Movimientos.
6. Presentar errores recuperables con un mensaje claro y conservar detalles técnicos en el registro de diagnóstico.

**Criterio de aceptación:** la pantalla nunca queda en blanco sin explicación; los filtros se mantienen al regresar a la vista o al actualizarse datos locales; un error no se presenta como «Sin resultados».

### Etapa 3 — Reforzar la exactitud de consultas

1. Verificar fechas inicial/final iguales, periodos invertidos, cambio de mes/año y año bisiesto.
2. Mantener la interpretación inclusiva de fechas locales y los límites UTC exclusivos en la consulta.
3. Definir una zona horaria consistente para el negocio si los dispositivos pueden tener configuraciones horarias diferentes.
4. Revisar las uniones con usuarios y categorías; cuando falte una relación, mostrar el registro con un texto de respaldo o reportar una anomalía, en vez de omitirlo silenciosamente.
5. Confirmar que las actualizaciones recibidas por sincronización recarguen los reportes sin alterar filtros elegidos.

**Criterio de aceptación:** ventas y movimientos de los límites del periodo aparecen una sola vez, y registros locales incompletos no desaparecen sin indicación.

### Etapa 4 — Rendimiento y revisión multiplataforma

1. Medir consultas con un historial amplio.
2. Si el volumen lo requiere, añadir paginación o carga incremental para ventas y movimientos.
3. Revisar tamaños, contraste, foco con teclado y etiquetas accesibles en Windows; revisar áreas táctiles, desplazamiento y barra inferior en Android.
4. Validar cada etapa con recorridos de ventas, inventario y movimientos en ambas plataformas.

**Criterio de aceptación:** la pantalla sigue respondiendo al consultar periodos extensos y los controles pueden usarse con ratón, teclado y tacto según el dispositivo.

## Alcance que se conserva

Se mantienen las tres consultas actuales, el uso de datos SQLite locales, los filtros acordados y el aviso de posibles operaciones pendientes de sincronización. Este plan no incorpora ganancia, margen, costos, cobros, rankings, gráficos ni exportación, que la especificación vigente excluye.

## Evidencia y límites de esta auditoría

- La captura muestra los campos de fecha y el área vacía; el usuario reporta que el calendario no se abre o no permite elegir.
- El código confirma reinicio de filtros, descarte de una consulta concurrente y lectura sin paginación.
- La causa del selector y del estado vacío requiere reproducción interactiva; esta revisión no ejecutó la aplicación ni cambió el código.
- `TASK.md` y `MauiApp1/ARCHITECTURE.md` registran una verificación anterior de consultas y compilación. Esa evidencia no comprueba la interacción actual del calendario ni los estados visuales observados ahora.
- No se encontraron pruebas automatizadas específicas del módulo de reportes en el árbol revisado.
