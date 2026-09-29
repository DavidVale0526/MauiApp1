# Fase 6 — Consultas, reportes y alertas

## T6.1 — Definir reportes requeridos

### Decisiones documentadas

- **Ventas:** fecha/hora local, referencia corta, usuario, modalidad contado/crédito, cliente si es crédito y total individual en córdobas. Rango inclusivo de fechas locales y filtros por modalidad/cliente. El total describe ventas registradas, no cobros.
- **Inventario:** fotografía a la hora de consulta con producto, categoría, estado activo/inactivo, existencia/unidad y nivel mínimo/unidad. Filtros por categoría y estado; no hay periodo ni valoración monetaria.
- **Movimientos:** fecha/hora local, snapshots históricos de producto/unidad, tipo, cantidad, motivo, referencia corta y usuario. Rango inclusivo de fechas locales, filtros por producto/tipo y sin agregados derivados.
- Los reportes se rotulan «Datos locales de este dispositivo; pueden incluir operaciones aún no sincronizadas».
- Quedan fuera ganancia/margen, costos y valoración, ranking, cobros, gráficos y exportación hasta aprobación específica.
- T6.3: alerta para existencia menor o igual al mínimo; muestra en Inicio y Consulta de existencias, únicamente para productos activos.

### Archivos modificados

- `D:\Trabajo\Tesis\TASK.md` — T6.1 marcada con las decisiones; T6.3 sigue pendiente.
- `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — especificación del Módulo 6 y sus exclusiones.
- `MauiApp1/ARCHITECTURE.md` — contrato funcional previsto para la implementación de T6.2.
- `HISTORIAL_CAMBIOS/README.md` — índice de fases.

### Verificación

- Revisión cruzada de los campos, periodos, filtros y exclusiones entre la lista, el plan y la arquitectura.
- T6.1 es una definición funcional; no cambió código ni requirió compilación.

## T6.2 — Construir reportes y consultas

### Creados

- `MauiApp1.Application/Reports/IReportService.cs`, `SalesReportRow.cs`, `InventoryReportRow.cs`, `MovementReportRow.cs` — contratos de lectura del reporte.
- `MauiApp1.Infrastructure/Reports/EfReportService.cs` — consultas autenticadas SQLite de ventas, inventario y movimientos, con filtros aplicados en la consulta.
- `MauiApp1/Presentation/ViewModels/ReportsViewModel.cs` — filtros, manejo de periodos, presentación de datos y vacíos.
- `MauiApp1/Presentation/Views/ReportsPage.xaml`, `.xaml.cs` — pantalla de tres reportes.

### Modificados

- `MauiApp1.Infrastructure/Persistence/LocalPersistenceServiceCollectionExtensions.cs` — registro del servicio de reportes.
- `MauiApp1/MauiProgram.cs`, `MauiApp1/Presentation/Views/MainPage.xaml`, `.xaml.cs` — DI y navegación desde el menú principal.
- `ARCHITECTURE.md`, `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — comportamiento realizado y evidencia.

### Verificación

- Infraestructura, MAUI Windows y Android compilaron con 0 advertencias y 0 errores.
- Smoke check SQLite pasó periodo y filtros de ventas (modalidad/cliente), inventario (categoría/estado) y movimientos (producto/tipo), con actor y datos históricos.
- Revisión visual manual de los tres reportes por el usuario pendiente.

## T6.3 — Implementar nivel mínimo y alerta

### Decisiones aprobadas

- Disparar alerta cuando la existencia sea igual o menor al mínimo configurado para productos activos; no incluir productos inactivos.
- Mostrar alertas en Inicio y Consulta de existencias. Refrescar al abrir esas pantallas y al actualizar manualmente inventario; no usar notificaciones en segundo plano.
- El mínimo editable ya existe desde T2.2; esta tarea reutiliza ese dato y no añade almacenamiento/campos redundantes.

### Creados

- `MauiApp1.Application/Products/LowStockProductQuery.cs` — criterio compartido que selecciona y ordena productos activos en o bajo el mínimo.

### Modificados

- `MauiApp1/Presentation/ViewModels/MainPageViewModel.cs`, `MauiApp1/Presentation/Views/MainPage.xaml`, `.xaml.cs` — carga de alertas al aparecer Inicio y panel con detalle.
- `MauiApp1/Presentation/ViewModels/StockConsultationViewModel.cs`, `MauiApp1/Presentation/Views/StockConsultationPage.xaml` — resumen visible que se recalcula al abrir/refrescar inventario.
- `D:\Trabajo\Tesis\TASK.md`, `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md`, `ARCHITECTURE.md` — requisitos realizados y verificación.

### Verificación

- Windows (`net10.0-windows10.0.19041.0`) y Android (`net10.0-android`) compilaron con 0 advertencias y 0 errores.
- Revisión visual manual de las alertas y casos de existencia igual/menor al mínimo pendiente por el usuario.
