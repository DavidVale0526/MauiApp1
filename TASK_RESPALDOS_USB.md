# TASK — Respaldos locales en memoria USB

**Estado general:** En progreso — implementación de etapas 1–6; pendiente validar el flujo integral con USB física y coordinador instalado
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
- [x] Definir cómo se pausa y reanuda el tráfico de escritura y cómo se recupera el servicio ante una restauración fallida; la implementación y verificación están pendientes en la etapa 5.
- [x] Documentar los hallazgos y actualizar esta tarea antes de marcar la etapa completa.

**Criterio de aceptación:** el diseño de respaldo y destino está acordado. La etapa de auditoría queda completada; la verificación práctica del procedimiento de restauración pertenece a las etapas 5 y 7.

### Hallazgos y decisiones de la auditoría

- La aplicación Windows Host y el servicio `MiscelaneaAriCoordinator` usan `miscelanea-ari-central.db` bajo `Coordinator.DataDirectory`, normalmente dentro de `C:\ProgramData\Miscelanea Ari\Coordinator`.
- La base central usa el modelo `LocalDbContext` y añade las tablas del coordinador para vinculación y sincronización. El respaldo completo cubre catálogo, inventario, ventas, cuentas, dispositivos, conflictos y operaciones/estado de sincronización.
- `SqliteConnection.BackupDatabase` ya se usa en la inicialización e importación; el actualizador genera una copia central antes de detener el servicio. No existe respaldo recurrente accesible al usuario.
- Los endpoints administrativos existentes validan dispositivo autorizado, sesión central y rol Administrador. Las futuras operaciones de respaldo deben conservar esas comprobaciones; `/health` no debe aceptar operaciones de datos.
- Decisión: cada respaldo tendrá nombre único por UTC, archivo temporal, metadatos JSON escritos al finalizar y verificaciones de `PRAGMA integrity_check`, tamaño y SHA-256. USB ausente, sin permisos o con poco espacio produce error/omisión visible, nunca una copia oculta en disco interno.
- Retención inicial propuesta: 30 diarios, 12 semanales y 12 mensuales, sin borrar el último respaldo válido.
- Límite de detección actual: Windows reporta algunas unidades flash como extraíbles, pero discos USB externos pueden aparecer como fijos. Estos últimos no aparecerán hasta implementar y verificar una detección por bus físico.
- La restauración usa un auxiliar elevado y no reemplaza la base mientras Host o el servicio acepten escrituras; la prueba en una instalación real sigue pendiente.

### Protocolo elegido para implementar la restauración (Windows Host)

La restauración no reemplazará la base desde el proceso MAUI que la mantiene abierta. Se implementará como una operación de recuperación coordinada, solicitada por el Administrador desde la interfaz y ejecutada por un auxiliar elevado de Windows:

1. La interfaz solo habilitará “Restaurar” después de validar la copia y confirmar que coincide exactamente con el esquema actual. Al pulsarlo mostrará el archivo, fecha, tamaño y la advertencia de pérdida; exigirá confirmación explícita.
2. Tras aceptar UAC, la app Host cerrará su ventana y el auxiliar esperará a que termine ese proceso. Si no puede cerrarse, abortará sin tocar la base.
3. El auxiliar comprobará que la ruta del respaldo queda dentro de `Miscelánea Ari/Respaldos` de la USB seleccionada por identidad de volumen; no aceptará una ruta arbitraria.
4. Detendrá el servicio coordinador y esperará hasta 30 segundos. Si no se detiene, abortará sin modificar la base.
5. Con ambos escritores detenidos, el ejecutable del coordinador creará una copia de emergencia mediante SQLite Backup API dentro de `ProgramData/.../Backups/PreRestore-*`, la validará y la conservará aunque el proceso posterior falle.
6. Copiará la base elegida a un archivo temporal en el mismo directorio central, volverá a validar ficha, SHA-256, integridad y esquema, y solo entonces moverá la base actual a un nombre de recuperación y promoverá el temporal mediante renombrado en el mismo volumen.
7. Iniciará el servicio y comprobará `/health`. Si falla, detendrá el servicio, restaurará automáticamente la copia de emergencia y volverá a iniciar el coordinador. No borrará copias de emergencia automáticamente.
8. Si la recuperación termina correctamente, informará que el Administrador debe abrir de nuevo la app Host. Los dispositivos cliente recibirán la base restaurada al reconectarse; los cambios pendientes que nunca llegaron al coordinador requieren la advertencia previa.

La interfaz no debe presentar una restauración como disponible hasta que el auxiliar, su empaquetado y la ruta de recuperación automática estén implementados y revisados.

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
- [x] Requerir confirmación explícita del administrador, incluyendo fecha y nombre de la copia elegida.
- [x] Evitar escrituras remotas durante la restauración deteniendo el coordinador; los clientes quedan temporalmente sin conexión y no pueden enviar operaciones.
- [x] Cerrar la aplicación Host y detener el servicio antes de reemplazar la base; el auxiliar aborta si Host no cierra o el servicio no se detiene.
- [x] Crear y validar un respaldo de emergencia de la base actual mediante SQLite Backup API antes de modificarla.
- [x] Restaurar mediante SQLite Backup API a un archivo temporal y promoción por renombrado atómico con los escritores detenidos; conservar la base desplazada y la emergencia.
- [x] Exigir coincidencia exacta del esquema e identidad del coordinador; validar integridad SQLite y tablas centrales antes de promover.
- [x] Reiniciar el servicio, comprobar `/health` y presentar resultado; el Administrador vuelve a abrir Host manualmente.
- [x] Si falla el inicio o `/health`, detener el servicio, recuperar la copia de emergencia y volver a comprobar `/health`.
- [ ] Documentar el tratamiento de secuencias y operaciones pendientes de dispositivos para evitar aplicar dos veces cambios tras la recuperación.

**Avance parcial:** la pantalla ya permite iniciar la restauración tras validar una copia compatible y confirmar el nombre/fecha y la pérdida de cambios. Un auxiliar de Windows solicita elevación, espera el cierre de Host, valida USB/GUID y configuración del servicio, crea la emergencia, restaura y recupera automáticamente ante fallo. La etapa no se cierra hasta verificarla con una USB y una instalación coordinadora reales, y documentar cómo tratar operaciones pendientes de otros dispositivos.

**Criterio de aceptación:** una restauración válida devuelve el coordinador a un estado operativo; ante fallo, se puede volver a la copia de emergencia sin improvisar ni perder la base actual.

## Etapa 6 — Cifrado y recuperación de la clave

- [x] Decidir si el cifrado se incluye en la primera entrega o en una fase posterior; se incluirá antes de cerrar el módulo. Los respaldos antiguos sin cifrar se identificarán como tales.
- [x] Cifrar cada archivo completo con un formato autenticado de flujo y derivar la clave de recuperación con PBKDF2-SHA256; no guardar la frase junto al respaldo.
- [x] Diseñar cómo el administrador crea y conserva la clave de recuperación fuera de la USB y fuera de la misma PC.
- [x] Advertir y exigir reconocimiento de que perder la clave impide restaurar.
- [ ] Probar detección de contraseña incorrecta, archivo alterado y archivo truncado antes de iniciar cualquier restauración.
- [x] No prometer “extremo a extremo”: la copia se cifra en la PC coordinadora y se descifra allí al restaurar.

### Decisiones de diseño para el cifrado

- La frase de recuperación será elegida por el Administrador (mínimo 12 caracteres). No se guardará en texto claro ni en la USB.
- Se generará una clave aleatoria de datos de 256 bits. La frase la protegerá en una ficha mediante AES-256-GCM; PBKDF2-SHA256 usará sal aleatoria de 16 bytes y 600 000 iteraciones.
- Windows protegerá localmente la clave aleatoria con DPAPI en modo máquina para que el servicio coordinador pueda crear respaldos programados sin una sesión abierta. La frase permitirá recuperar esa clave en una instalación nueva.
- El cifrado de la base usará un formato de flujo autenticado AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC) para no cargar toda la base en memoria. La MAC se verificará antes de descifrar.
- Cada ficha de respaldo llevará la envoltura cifrada de la clave, los parámetros y la MAC de metadatos. No contendrá la frase ni la clave de datos en claro.
- El cifrado protege los archivos de respaldo en la USB. La base central activa y el archivo temporal de trabajo en la carpeta protegida del coordinador permanecen como SQLite normal.
- Las copias `.db` ya existentes no se alterarán ni se presentarán como cifradas; la interfaz indicará claramente cuáles son antiguas y sin cifrar.
- Si se pierde la frase, las copias no podrán recuperarse en otra PC. La interfaz deberá pedir al Administrador que confirme que guardó la frase fuera de la USB y de la PC coordinadora.

**Progreso técnico:** implementado el cifrado AES-256-CBC + HMAC-SHA256 en flujo; la clave aleatoria se envuelve con AES-256-GCM y PBKDF2-SHA256, y su copia local se protege con DPAPI para el usuario y el servicio coordinador. La ficha cifra su envoltura y autentica sus metadatos con HMAC. La pantalla permite configurar la frase una sola vez, exige confirmar que se guardó fuera de la PC/USB y permite desbloquear la clave a partir de una copia cifrada en otro equipo. Respaldos manuales y programados nuevos se cifran; la validación comprueba la MAC del archivo y la ficha antes de descifrar, y la restauración recibe únicamente una copia autenticada. Los respaldos `.db` anteriores se conservan y aparecen como «sin cifrar». Falta probar en USB real la frase incorrecta, la alteración/truncamiento, el acceso del servicio desatendido y una restauración cifrada desde otra instalación.

**Criterio de aceptación:** la confidencialidad, integridad y custodia de la clave están descritas y una copia cifrada puede restaurarse en un procedimiento documentado.

## Etapa 7 — Validación integral y guía de uso

- [ ] Verificar respaldo mientras existen lecturas y escrituras concurrentes del coordinador.
- [ ] Verificar creación con USB llena, retirada, solo lectura, sin permisos y con carpeta borrada.
- [ ] Verificar reconexión con letra de unidad distinta y varias USB conectadas.
- [ ] Verificar programación con la USB conectada y ausente, reinicio del servicio y cambio de horario.
- [ ] Restaurar una copia válida en un entorno aislado y comprobar catálogo, existencias, ventas, usuarios y datos de sincronización.
- [x] Verificar a nivel criptográfico que una frase incorrecta, una ficha manipulada, un archivo cifrado alterado/truncado y una clave incorrecta sean rechazados antes de restaurar.
- [ ] Verificar en el ejecutor instalado una copia incompatible y una restauración cancelada sin tocar la base en uso.
- [ ] Confirmar permisos: cajero no puede administrar copias ni restaurar; cliente Android no ve controles centrales que no puede ejecutar.
- [x] Compilar Windows y Android; Android mantiene el resto de la aplicación sin cambios y no ofrece la administración de la USB de la PC coordinadora.
- [x] Actualizar el paquete de Windows, las instrucciones de instalación y la guía breve para el usuario final.

### Evidencia de validación — 26/10/2026

- [x] `MauiApp1.Infrastructure` y `MauiApp1.Coordinator` compilaron en Release para `win-x64` como parte de la publicación del instalador.
- [x] MAUI compiló para Windows al generar el instalador y para Android con `dotnet build ... --no-restore -f net10.0-android`; ambos terminaron sin advertencias ni errores.
- [x] Un ensayo temporal con los componentes criptográficos reales verificó cifrado/descifrado de 3 MB, rechazo de frase y clave incorrectas, rechazo de MAC de ficha alterada, y rechazo de archivo modificado o truncado. El ensayo temporal fue retirado del repositorio.
- [x] Los scripts de instalación, actualización, restauración y construcción del instalador pasaron análisis sintáctico de PowerShell.
- [x] El instalador publicado incluye el ejecutor de restauración, el coordinador actualizado y `GUIA_RESPALDOS.md`. Paquete: `artifacts/windows-installer/MiscelaneaAri-20261026-183954.zip`.
- [ ] No se ejecutó una restauración real ni la programación del servicio en una instalación coordinadora activa; tampoco se conectó una USB física en este entorno. Estos puntos requieren pruebas de operación antes de cerrar la etapa.

**Criterio de cierre:** todas las rutas críticas de creación y restauración están verificadas; la interfaz distingue respaldo válido, omitido, fallido y restaurado, y el usuario dispone de instrucciones para mantener la USB conectada durante la tarea programada. La etapa sigue abierta hasta realizar las pruebas físicas y de restauración señaladas arriba.

## Registro de progreso

| Fecha | Etapa | Resultado / evidencia | Responsable |
|---|---|---|---|
| 26/10/2026 | Etapa 0 | Auditados ruta, alcance de la base central, autenticación existente, API SQLite, fallos de destino y política de retención. El protocolo detallado para detener escritores y recuperar el servicio se definió en etapa 5. | Agente de programación |
| 26/10/2026 | Etapa 1 | Añadidos contrato y proveedor Windows para enumerar USB extraíbles con GUID, etiqueta, montaje y espacio; proveedor registrado en DI. Faltan selección persistente, estados de UI y unidades USB clasificadas por Windows como fijas. | Agente de programación |
| 26/10/2026 | Etapa 2 | Añadido `SqliteCentralDatabaseBackupService`: copia SQLite a archivo temporal, comprueba integridad/esquema, calcula SHA-256, escribe manifiesto y renombra el archivo. Registrado en DI de Windows. Compilación Windows y Android correcta; faltan pruebas con una memoria USB real y reporte visible en interfaz. | Agente de programación |
| 26/10/2026 | Etapa 3 | Añadida pantalla “Respaldos” para Administrador en Windows Host; permite elegir y recordar la USB por GUID, crear respaldos manuales, consultar manifiestos e incompletos, ver estado/tamaño/versiones y abrir la carpeta. Compilaciones Windows y Android correctas; falta revisión en ejecución con USB física. | Agente de programación |
| 26/10/2026 | Etapa 4 | Añadidos selectores de frecuencia/hora/día, persistencia compartida con el servicio, ejecución desatendida desde el coordinador, estado de último intento y retención segura de respaldos automáticos. Pendiente validar compilación y probar en PC/USB real. `dotnet build`/`restore` se detuvo antes de compilar fuentes: MSBuild no pudo resolver sus SDK locators de workloads; reportó 0 errores de código, por lo que no se considera compilación aprobada. | Agente de programación |
| 26/10/2026 | Etapa 5 — ejecutor | Agregada restauración con confirmación explícita y auxiliar Windows elevado. El ejecutor espera el cierre de Host, rechaza otras instancias Windows, detiene el coordinador, comprueba identidad GUID de USB, hash, esquema e identidad del coordinador, conserva una emergencia y usa SQLite Backup API + archivo temporal con promoción atómica. El coordinador verifica además que el servicio esté detenido y aborta si siguen presentes archivos `-wal`/`-shm`. Si no arranca o falla `/health`, restaura la emergencia y verifica de nuevo. Sintaxis PowerShell correcta; compilación de `MauiApp1.Coordinator`, `MauiApp1.Infrastructure`, Windows MAUI y Android correcta, 0 advertencias/errores (`BuildProjectReferences=false`). La prueba real con USB y servicio detenido sigue pendiente. | Agente de programación |
| 26/10/2026 | Etapa 6 — diseño y envoltura | Acordado AES-256-CBC + HMAC-SHA256 para cifrar la base en flujo, clave de datos aleatoria de 256 bits protegida localmente con DPAPI y recuperación mediante frase PBKDF2-SHA256 (600 000 iteraciones). Añadido el contrato y el código AES-GCM para envolver/desenvolver la clave. La integración de archivos, DPAPI, programación, restauración e interfaz queda pendiente. | Agente de programación |
| 26/10/2026 | Etapa 6 — integración | Conectado el cifrado autenticado de archivo y ficha a respaldos manuales/programados, historial, validación y restauración; añadida configuración de frase con confirmación de custodia y desbloqueo desde una ficha cifrada. Copias `.db` antiguas se muestran sin cifrar. Compilaron `MauiApp1.Infrastructure`, `MauiApp1.Coordinator` y Windows MAUI con 0 advertencias/errores. Falta verificación de ejecución, USB real y pruebas de daño/frase incorrecta; la etapa sigue abierta hasta cubrirlas. | Agente de programación |
| 26/10/2026 | Etapa 7 — validación y guía | Compilaron Windows/Android; el ensayo criptográfico rechazó frase/clave incorrectas, ficha manipulada y archivos alterados/truncados. Pasaron los análisis sintácticos de scripts PowerShell. El instalador `MiscelaneaAri-20261026-183954.zip` contiene coordinador, restaurador y guía. No se desplegó sobre el coordinador activo ni se probaron USB física, tarea programada o restauración real; esos casos siguen pendientes, así que la etapa no se cierra aún. | Agente de programación |
