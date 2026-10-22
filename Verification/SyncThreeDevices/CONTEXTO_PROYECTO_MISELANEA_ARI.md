# CONTEXTO DE TRABAJO_Miscelanea Ari

## 1. Objetivo actual
Adaptar las interfaces ya implementadas en un proyecto .NET MAUI a los diseños de Figma de Miscelanea Ari, tanto en Windows como en Android. La tarea asignada consiste principalmente en **adaptacion visual y responsive**, no en reescribir los modulos ni la arquitectura existente.

## 2. Tecnologias y organizacion
- Proyecto: `MauiApp1` (.NET MAUI 10 / C# / XAML; Windows y Android).
- Archivo principal del Dashboard: `MauiApp1/Presentation/Views/MainPage.xaml`.
- Codigo asociado: `MauiApp1/Presentation/Views/MainPage.xaml.cs`.
- Componente base de navegacion movil: `MauiApp1/Presentation/Views/CoordinatorRefreshPage.cs` (comprobar implementacion exacta en el repositorio actual).
- Revisar tambien `AppShell.xaml`, estilos en `Resources/Styles` y los `ViewModels` que correspondan.
- Arquitectura de negocio prevista: una computadora Windows en el local aloja MySQL y el servidor/API local; dispositivos Android por Wi-Fi/LAN. IMPORTANTE: el repositorio compartido tambien contiene funcionalidad existente de sincronizacion/coordinador; **no eliminarla ni rediseñarla sin consultar al equipo**, ya que puede estar en desarrollo y diferir de la propuesta conceptual.

## 3. Referencias visuales aprobadas para el Dashboard
**Escritorio:** menu lateral azul marino; encabezado superior con estado de conexion, notificaciones y perfil; titulo Inicio; boton Nueva Venta Rapida; 4 KPI horizontales (Ventas de hoy, Stock bajo, Proximos vencimientos, Efectivo en caja); grafico de ventas ultimos 7 dias y alertas operativas en columnas; tabla de ultimas operaciones.

**Movil:** encabezado compacto con identidad, conexion, notificaciones y usuario; titulo Inicio y boton Nueva venta; indicadores en cuadricula 2x2; grafico vertical; alertas; operaciones recientes en tarjetas; barra inferior con Inicio, Ventas, Inventario, Reportes, Mas. **No duplicar la barra inferior**.

Las capturas de Figma son referencias de diseño: cifras y registros de ejemplo no deben confundirse con datos reales de produccion.

## 4. Progreso concreto de la adaptacion
- Se preparo una primera propuesta de reemplazo de `MainPage.xaml` y `MainPage.xaml.cs` con cuatro KPI y algunas adaptaciones responsive.
- Al probar en Android, aparecio una **barra inferior duplicada**: una añadida en `MainPage.xaml` y otra ya existente en el componente base `CoordinatorRefreshPage.cs`.
- La solucion elegida fue **conservar la barra inferior compartida preexistente y eliminar solo la barra adicional de `MainPage`**. Se preparo una correccion; verificar en el repositorio local si esos cambios ya estan aplicados. No asumir que el archivo ZIP anterior refleja el estado mas reciente del clon.
- Los indicadores añadidos mostraban `Sin datos`, y algunas secciones antiguas aun aparecian debajo de las tarjetas. Esto era temporal.
- La siguiente fase del Dashboard es adaptar grafico semanal, alertas operativas y operaciones recientes, antes de conectar todos los KPI a fuentes reales.

## 5. Paginas y procesos contemplados
- Ventas: Punto de venta -> Confirmar venta (modal) -> Factura/comprobante (modal); Historial de ventas -> Detalle (modal).
- Inventario: Productos -> Nuevo/Editar producto (modal); Entradas -> Nueva entrada (modal); Salidas -> Nueva salida (modal); Vencimientos -> Detalle/accion (modal); Movimientos -> Detalle (modal).
- Proveedores -> Nuevo/Editar/Detalle (modal).
- Caja -> Apertura de turno / Cierre de turno (modales), segun estado.
- Reportes: ventas, inventario, compras/entradas, mermas, caja.
- Administracion: usuarios, conexion y dispositivos, configuracion, respaldo/restauracion (destino de USB conectado a la PC con MySQL).
- La pantalla de categorias debe alimentar el selector de categoria de productos. Las fechas de vencimiento pueden variar por lote/entrada del mismo producto.

## 6. Reglas de interfaz
- Misma funcionalidad de negocio en Windows y Android, composicion visual adaptable.
- Windows: sidebar, KPI en fila, grafico/alertas lado a lado, operaciones en tabla.
- Android: barra inferior existente compartida, KPI 2x2, una columna, operaciones en tarjetas, formularios en una columna.
- Evitar menus repetidos: si hay barra inferior compartida, no añadir otra en una pantalla concreta.
- Evitar numeros de ejemplo en la logica; usar propiedades reales o placeholders honestos hasta implementar servicios.
- Mantener nombres de metodos, eventos `Clicked`, bindings, ViewModels, servicios y navegacion existentes a menos que se acuerde el cambio.
- No sustituir mecanicamente archivos completos si el repositorio cambio; inspeccionar diferencias con Git.

## 7. Reglas de trabajo para el asistente de VS Code
1. Antes de editar, inspecciona la estructura actual y lee archivos completos relevantes; **no presupongas** que coinciden con ZIPs o resumenes anteriores.
2. Resume que vas a cambiar, que funcionalidad preservaras y que dependencias existen.
3. Adapta **una seccion a la vez** y explica con claridad cada archivo modificado.
4. Si ya existe navegacion global, reutilizala; no crees una segunda barra en `MainPage`.
5. No alteres backend, MySQL, red, sincronizacion o flujos de ventas sin autorizacion.
6. Intenta compilar con el target adecuado y reporta errores reales; nunca afirmes que compilo si no se ejecuto el comando.
7. Si falta un diseño de Figma o especificacion, pide la captura concreta o trabaja solo en las partes verificadas.
8. Usa ramas de Git y cambios pequeños, revisables y reversibles; evita sobrescribir avances de otros integrantes.

## 8. Primer encargo para esta sesion
Verificar el Dashboard actual de Windows/Android, asegurar que la barra inferior movil aparezca una sola vez, y proponer/implementar la siguiente seccion de Figma: **grafico de ventas de 7 dias + alertas operativas**, conservando navegacion y logica existente. Antes de escribir codigo, muestra los archivos encontrados y el plan de cambios.

## 9. Alcance de este documento
Este resumen proviene de decisiones tomadas en una conversacion de planificacion. **No garantiza el estado actual del codigo** ni equivale a acceso directo a Figma. El repositorio local, sus pruebas y capturas mas recientes prevalecen si hay diferencias; ante decisiones conflictivas, consultar antes de eliminar funciones.
