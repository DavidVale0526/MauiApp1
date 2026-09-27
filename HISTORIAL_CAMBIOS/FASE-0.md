# Fase 0 — Revisión del proyecto y decisiones previas

## T0.1 — Inspección

- No se añadieron ni modificaron archivos del proyecto; se inspeccionó la solución inicial, documentación disponible, configuración y destinos.
- Evidencia anotada en `D:\Trabajo\Tesis\TASK.md` bajo T0.1.

## T0.2 — Decisiones de arquitectura y alcance

- `D:\Trabajo\Tesis\TASK.md` — documenta decisiones confirmadas, dependencias y preguntas aún pendientes para los módulos posteriores.
- `D:\Trabajo\Tesis\PLAN_MODULOS_SISTEMA_MISCELANEA_ARI.md` — plan funcional detallado ajustado a .NET MAUI, almacenamiento SQLite local y central, coordinador LAN, operación offline-first y sincronización.

## Decisiones registradas

- Aplicación MAUI compartida para Windows y Android, con MVVM y capas separadas.
- SQLite/EF Core local por dispositivo y base central accedida solo mediante un servicio coordinador Windows.
- Comunicación de LAN mediante API y SignalR; la aplicación conserva operaciones localmente cuando el coordinador/red no está disponible.
- Cuentas individuales de Administrador y Cajero sobre los mismos datos del negocio; la matriz exacta de permisos y el alta inicial quedaron para T1.3.
- Las reglas de otros módulos que no están confirmadas permanecen pendientes y no deben asumirse.

