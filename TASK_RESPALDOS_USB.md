# TASK — Respaldos locales en memoria USB

**Estado general:** En progreso — etapas 1–4 implementadas; faltan verificación física, restauración y cifrado  
**Plataforma inicial:** Windows, en la PC principal configurada como coordinador  
**Referencia visual:** módulo de respaldos compartido por el usuario el 26 de octubre de 2026

## Objetivo

Permitir que el administrador cree, programe, consulte y restaure respaldos de la base central del negocio en una memoria USB conectada físicamente a la PC coordinadora. El flujo debe trabajar sin Internet y mantener intacto el uso actual de SQLite y la sincronización LAN.

## Alcance y decisiones de seguridad

- [x] El respaldo principal representa la base central completa del negocio, que reside en la PC coordinadora; no presentarlo como copia de las bases locales de todos los dispositivos.
- [x] La primera versión de la interfaz de administración se habilita solo en Windows cuando la aplicación está en modo Host y el usuario es Administrador.
- [x] Guardar el destino como volumen USB identificado y una carpeta dentro de él; no confiar solo en la letra de unidad, que puede cambiar.
- [x] No aceptar unidades de red ni rutas desconectadas como si fueran una USB disponible.
- [x] Crear la copia usando la API de respaldo de SQLite para obtener una instantánea coherente aun con WAL; no copiar el `.db` activo ni manipular manualmente sus archivos `-wal` y `-shm`.
- [x] Nunca sustituir una copia válida con una copia incompleta. Crear en nombre temporal, validar y luego renombrar al nombre definitivo.
- [ ] Antes de restaurar, generar y validar una copia de emergencia del estado central actual en el destino disponible.
- [ ] Pausar escrituras, cerrar la aplicación de escritorio Host cuando corresponda y coordinar el servicio Windows antes de reemplazar/restaurar la base.
- [x] La pérdida o desconexión de la USB debe producir un estado visible y registro del fallo; no guardar silenciosamente en otro disco.
- [ ] No describir el cifrado del archivo como “cifrado de extremo a extremo”. Acordar primero el modelo de cifrado, custodia y recuperación de clave. Si la clave se pierde, el respaldo cifrado debe declararse irrecuperable.
- [ ] No alterar el formato de las operaciones de sincronización, identificadores, secuencias, usuarios ni permisos como parte de este módulo.
- [x] Conservar los respaldos preventivos existentes durante actualización e importación; el módulo nuevo los complementa.

## Etapa 0 — Auditoría técnica y contrato del respaldo

- [x] Confirmar la ruta efectiva de la base central usada por el servicio y por la aplicación Host.
- [x] Confirmar qué tablas y datos relacionados residen en esa base, incluidos usuarios, dispositivos, operaciones, conflictos y estado de sincronización.
- [x] Revisar autenticación/autorización disponible entre la interfaz Windows y el servicio coordinador para exponer acciones administrativas de respaldo sin abrir un endpoint anónimo.
- [x] Revisar cómo detectar unidades extraíbles, obtener una identidad estable del volumen, comprobar espacio disponible y resolver su punto de montaje en Windows.
- [x] Definir formato/nombre de los archivos y metadatos laterales: identificador, fecha local y UTC, versión de aplicación/esquema, tamaño, resultado y SHA-256.
- [x] Definir comportamiento ante USB con poco espacio, retirada durante escritura, volumen con error, nombre duplicado y cambio de letra.
- [x] Definir retención configurable inicial: conservar 30 diarios, 12 semanales y 12 mensuales, evitando eliminar el último respaldo válido.
- [ ] Definir cómo se pausa y reanuda el tráfico de escritura y cómo se recupera el servicio ante una restauración fallida.
- [x] Documentar los hallazgos y actualizar esta tarea antes de marcar la etapa completa.

**Criterio de aceptación:** el diseño de respaldo y destino está acordado. Esta etapa sigue parcial: falta definir y verificar cómo pausar escrituras/restaurar cuando Host y servicio usan la misma base.

### Hallazgos y decisiones de la auditoría

- La aplicación Windows Host y el servicio `MiscelaneaAriCoordinator` usan `miscelanea-ari-central.db` bajo `Coordinator.DataDirectory`, normalmente dentro de `C:\ProgramData\Miscelanea Ari\Coordinator`.
- La base central usa el modelo `LocalDbContext` y añade las tablas del coordinador para vinculación y sincronización. El respaldo completo cubre catálogo, inventario, ventas, cuentas, dispositivos, conflictos y operaciones/estado de sincronización.
- `SqliteConnection.BackupDatabase` ya se usa en la inicialización e importación; el actualizador genera una copia central antes de detener el servicio. No existe respaldo recurrente accesible al usuario.
- Los endpoints administrativos existentes validan dispositivo autorizado, sesión central y rol Administrador. Las futuras operaciones de respaldo deben conservar esas comprobaciones; `/health` no debe aceptar operaciones de datos.
- Decisión: cada respaldo tendrá nombre único por UTC, archivo temporal, metadatos JSON escritos al finalizar y verificaciones de `PRAGMA integrity_check`, tamaño y SHA-256. USB ausente, sin permisos o con poco espacio produce error/omisión visible, nunca una copia oculta en disco interno.
- Retención inicial propuesta: 30 diarios, 12 semanales y 12 mensuales, sin borrar el último respaldo válido.
- Límite de detección actual: Windows reporta algunas unidades flash como extraíbles, pero discos USB externos pueden aparecer como fijos. Estos últimos no aparecerán hasta implementar y verificar una detección por bus físico.
- Pendiente definir el ciclo de mantenimiento/restauración. No reemplazar la base mientras la aplicación Host o el coordinador acepten escrituras.

## Etapa 1 — Servicio de respaldo y detección de USB

- [x] Crear el contrato de aplicación para enumerar y resolver destinos extraíbles por identidad de volumen.
- [x] Implementar en Windows la detección de unidades extraíbles listas y exponer nombre, espacio libre y ruta actual.
- [x] Usar el GUID del volumen como identidad estable y resolver el punto de montaje actual al volver a enumerar.
- [ ] Agregar selección persistente del destino y validación de carpeta dentro del volumen elegido; se hará junto con la interfaz de la etapa 3.
- [x] Mantener la lógica específica de Windows fuera de la capa de presentación.
- [x] Representar estados en la interfaz: USB no conectada, unidad seleccionada, creando/verificando, completado e incidencias de espacio, permisos o lectura.
- [ ] Definir contratos de creación, listado, validación y restauración junto con sus flujos, para que modelen la implementación real.

**Criterio de aceptación:** el servicio enumera unidades extraíbles listas, conserva la selección por GUID y vuelve a resolver el punto de montaje. La prueba de reconexión con una USB física queda en la etapa 7.

## Etapa 2 — Crear y verificar un respaldo manual

- [x] Añadir el servicio de infraestructura que invoca la API SQLite Backup para crear una instantánea consistente de la base central.
- [x] Crear el respaldo en un archivo temporal de la USB y finalizarlo mediante renombrado dentro del mismo volumen.
- [x] Ejecutar `PRAGMA integrity_check`, comprobar tablas centrales requeridas e historial de migraciones, calcular tamaño y SHA-256.
- [x] Escribir el manifiesto JSON después de validar la copia; una copia válida se conserva aunque falle la escritura de su manifiesto.
- [x] Volver a comprobar la identidad y el montaje de la USB antes y después de finalizar la copia; limpiar archivos temporales cuando el volumen siga disponible.
- [x] Prevenir dos respaldos simultáneos al destino mediante un bloqueo exclusivo que Windows libera al cerrar el proceso.
- [x] Añadir registro técnico de éxito o error sin registrar contraseñas, claves ni contenido de negocio.
- [ ] Persistir el resultado de una interrupción/desconexión y limpiar temporales huérfanos al reconectar; se completará con el historial de la etapa 3.
- [ ] Probar físicamente creación, falta de espacio, retirada de USB y contenido WAL en una memoria conectada; la verificación integral se hará en la etapa 7.

**Criterio de implementación:** servicio registrado en Windows y compilable, con instantánea SQLite, validación, manifiesto y manejo seguro de archivos temporales. La prueba con USB real y la presentación de fallos quedan pendientes para la etapa 7 y la etapa 3, respectivamente.

## Etapa 3 — Interfaz manual e historial

- [x] Agregar la opción “Respaldos” en el área administrativa existente, visible únicamente para Administrador en Windows Host.
- [x] Agregar accesos directos visibles desde el panel lateral y el panel principal de Windows Host; el enlace de “Más” se conserva.
- [x] Mantener el estilo visual actual de Miscelánea Ari; adaptar la jerarquía de la imagen de referencia sin copiar funciones que el sistema no tenga.
- [x] Mostrar último respaldo válido, destino USB, fecha, tamaño y estado de la última operación.
- [x] Añadir selección de USB; guardar la unidad elegida por GUID y usar la carpeta administrada `Miscelánea Ari/Respaldos` en su raíz.
- [x] Añadir botón “Crear respaldo” y mostrar su progreso y resultado.
- [x] Mostrar historial ordenado por fecha con estado, tamaño y versiones de aplicación/esquema.
- [x] Permitir abrir la carpeta del respaldo y actualizar la lista; no presentar una acción de descarga si el archivo ya está en el dispositivo.
- [x] Deshabilitar selección y acciones incompatibles mientras se crea o valida un respaldo.
- [x] Mostrar errores accionables para USB ausente, espacio insuficiente, permisos y fichas inconsistentes.
- [ ] Probar visualmente la pantalla y completar una operación con una memoria USB física conectada; registrar el resultado en la etapa 7.

**Criterio de implementación:** la pantalla y el flujo están conectados. La aceptación en ejecución queda pendiente hasta probar con la USB real en la PC coordinadora.

## Etapa 4 — Programación automática y retención

- [x] Añadir frecuencia diaria, semanal o mensual, hora local y activación/desactivación desde la interfaz.
- [x] Ejecutar la programación desde el servicio coordinador, incluso sin sesión interactiva de Windows y sin Internet.
- [x] Persistir la configuración y el resultado de la última ejecución en archivos JSON del directorio central compartido entre Host y servicio.
- [x] Resolver el volumen por su GUID en cada ejecución; si no está disponible, registrar el intento como omitido y mostrarlo en la interfaz.
- [x] Evitar repetir una ocurrencia ya procesada; coordinar respaldos manuales y programados mediante el bloqueo exclusivo por USB.
- [x] Aplicar la retención después de crear una copia válida: 30 diarios, 12 semanales y 12 mensuales; conservar los manuales y comprobar tamaño/ficha/hash antes de retirar copias anteriores.
- [x] Mostrar próximo intento, último intento y resultado del respaldo programado en la interfaz.
- [ ] Verificar en una PC coordinadora real la tarea con USB conectada/ausente, el reinicio del servicio, la concurrencia manual y la retención en medios físicos; se reserva para la etapa 7.

**Criterio de implementación:** la configuración de la interfaz alimenta el servicio, que crea copias verificadas sin sesión, marca omisiones y aplica retención por grupo. La aceptación en ejecución queda pendiente de la etapa 7.

## Etapa 5 — Restauración controlada

- [x] Permitir seleccionar una copia y ejecutar primero una validación no destructiva de ficha, tamaño, SHA-256, integridad SQLite y tablas requeridas.
- [x] Comparar la versión del esquema del respaldo con la base central actual; solo se declara compatible si coincide exactamente. Mostrar también las versiones registradas.
- [x] Explicar qué se perderá: cambios centrales posteriores a la fecha de la copia y posibles cambios aún no sincronizados desde dispositivos desconectados.
- [ ] Requerir confirmación explícita del administrador, incluyendo fecha y nombre de la copia elegida.
- [ ] Asegurar que los clientes no escriban durante la restauración: poner coordinador en mantenimiento/rechazar temporalmente operaciones nuevas y avisar a clientes conectados.
- [ ] Detener o aislar la aplicación Host y el servicio según el procedimiento seguro acordado en la etapa 0.
- [ ] Crear y validar un respaldo de emergencia de la base actual antes de modificarla.
- [ ] Restaurar mediante SQLite Backup API o sustitución segura con escritores detenidos; no borrar primero la única base existente.
- [ ] Aplicar migraciones solo si la compatibilidad definida lo permite y validar integridad, identidad del coordinador y tablas de sincronización.
- [ ] Reiniciar servicio y Host, comprobar `/health`, liberar mantenimiento y presentar resultado.
- [ ] Si falla cualquier paso, conservar la base de emergencia y ejecutar recuperación o instrucciones guiadas de retorno al estado previo.
- [ ] Documentar el tratamiento de secuencias y operaciones pendientes de dispositivos para evitar aplicar dos veces cambios tras la recuperación.

**Avance parcial:** selector y validación no destructiva completados. El botón/flujo de restauración sigue sin habilitarse hasta que exista un protocolo efectivo de mantenimiento que pause los escritores de la aplicación Host y del servicio, cree la copia de emergencia y pueda recuperar el servicio en caso de fallo. No se debe reemplazar la base en uso desde la pantalla actual.

**Criterio de aceptación:** una restauración válida devuelve el coordinador a un estado operativo; ante fallo, se puede volver a la copia de emergencia sin improvisar ni perder la base actual.

## Etapa 6 — Cifrado y recuperación de la clave

- [ ] Decidir si el cifrado se incluye en la primera entrega o en una fase posterior; los respaldos en USB sin cifrar no deben mostrar una insignia de “protegidos”.
- [ ] Si se implementa, cifrar cada archivo completo con un algoritmo autenticado y derivar la clave con un KDF adecuado; no guardar la clave junto al respaldo.
- [ ] Diseñar cómo el administrador crea y conserva la clave de recuperación fuera de la USB y fuera de la misma PC.
- [ ] Advertir y exigir reconocimiento de que perder la clave impide restaurar.
- [ ] Probar detección de contraseña incorrecta, archivo alterado y archivo truncado antes de iniciar cualquier restauración.
- [ ] No prometer “extremo a extremo”: la copia se cifra en la PC coordinadora y se descifra allí al restaurar.

**Criterio de aceptación:** la confidencialidad, integridad y custodia de la clave están descritas y una copia cifrada puede restaurarse en un procedimiento documentado.

## Etapa 7 — Validación integral y guía de uso

- [ ] Verificar respaldo mientras existen lecturas y escrituras concurrentes del coordinador.
- [ ] Verificar creación con USB llena, retirada, solo lectura, sin permisos y con carpeta borrada.
- [ ] Verificar reconexión con letra de unidad distinta y varias USB conectadas.
- [ ] Verificar programación con la USB conectada y ausente, reinicio del servicio y cambio de horario.
- [ ] Restaurar una copia válida en un entorno aislado y comprobar catálogo, existencias, ventas, usuarios y datos de sincronización.
- [ ] Verificar copia dañada, versión incompatible y restauración cancelada sin tocar la base en uso.
- [ ] Confirmar permisos: cajero no puede administrar copias ni restaurar; cliente Android no ve controles centrales que no puede ejecutar.
- [ ] Compilar Windows y Android; Android mantiene el resto de la aplicación sin cambios y no ofrece la administración de la USB de la PC coordinadora.
- [ ] Actualizar instalador/coordinador, instrucciones de actualización y guía breve para el usuario final.

**Criterio de cierre:** todas las rutas críticas de creación y restauración están verificadas; la interfaz distingue respaldo válido, omitido, fallido y restaurado, y el usuario dispone de instrucciones para mantener la USB conectada durante la tarea programada.

## Registro de progreso

| Fecha | Etapa | Resultado / evidencia | Responsable |
|---|---|---|---|
| 26/10/2026 | Etapa 0 | Auditados ruta, alcance de la base central, autenticación existente, API SQLite, fallos de destino y política de retención. El protocolo seguro de restauración sigue pendiente. | Agente de programación |
| 26/10/2026 | Etapa 1 | Añadidos contrato y proveedor Windows para enumerar USB extraíbles con GUID, etiqueta, montaje y espacio; proveedor registrado en DI. Faltan selección persistente, estados de UI y unidades USB clasificadas por Windows como fijas. | Agente de programación |
| 26/10/2026 | Etapa 2 | Añadido `SqliteCentralDatabaseBackupService`: copia SQLite a archivo temporal, comprueba integridad/esquema, calcula SHA-256, escribe manifiesto y renombra el archivo. Registrado en DI de Windows. Compilación Windows y Android correcta; faltan pruebas con una memoria USB real y reporte visible en interfaz. | Agente de programación |
| 26/10/2026 | Etapa 3 | Añadida pantalla “Respaldos” para Administrador en Windows Host; permite elegir y recordar la USB por GUID, crear respaldos manuales, consultar manifiestos e incompletos, ver estado/tamaño/versiones y abrir la carpeta. Compilaciones Windows y Android correctas; falta revisión en ejecución con USB física. | Agente de programación |
| 26/10/2026 | Etapa 4 | Añadidos selectores de frecuencia/hora/día, persistencia compartida con el servicio, ejecución desatendida desde el coordinador, estado de último intento y retención segura de respaldos automáticos. Pendiente validar compilación y probar en PC/USB real. `dotnet build`/`restore` se detuvo antes de compilar fuentes: MSBuild no pudo resolver sus SDK locators de workloads; reportó 0 errores de código, por lo que no se considera compilación aprobada. | Agente de programación |
| 26/10/2026 | Etapa 5 (parcial) | Añadida selección y validación no destructiva con hash, integridad SQLite, tablas requeridas y compatibilidad exacta de esquema. Se agregó advertencia sobre pérdida de datos posteriores. La restauración continúa bloqueada hasta diseñar y verificar mantenimiento seguro del Host y del servicio. Se recompiló correctamente `MauiApp1.Application` y se verificó que la DLL generada expone `ValidateAsync`; la compilación completa Windows sigue pendiente porque MSBuild se detiene antes de producir diagnósticos de código. También se añadieron accesos directos a Respaldos en el panel lateral y principal para Administrador en Windows Host. | Agente de programación |
