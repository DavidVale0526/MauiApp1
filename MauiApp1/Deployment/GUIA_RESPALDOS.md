# Guía de respaldos y recuperación

Esta función guarda la base central del negocio en una memoria USB conectada a la PC coordinadora. El respaldo de la USB se cifra; la base activa de Windows sigue siendo una base SQLite normal.

## Crear el primer respaldo

1. Entra como **Administrador** en la PC coordinadora.
2. Abre **Más → Respaldos** o usa el botón **Respaldos de la base central** del panel principal.
3. La primera vez, escribe una frase de recuperación de al menos 12 caracteres y confírmala.
4. Anota la frase en un lugar seguro que no sea esta PC ni la USB. Confirma en pantalla que ya la guardaste. La aplicación no almacena la frase; si se pierde, no se podrán recuperar las copias cifradas en otro equipo.
5. Conecta la memoria USB a la PC coordinadora, selecciónala en la lista y pulsa **Crear respaldo**.

Las copias nuevas terminan en `Miscelánea Ari\Respaldos` dentro de la USB y tienen extensión `.db.enc`. La ficha `.json` junto al archivo contiene los datos necesarios para comprobar y recuperar la copia, pero no la frase ni la clave en claro. Los respaldos `.db` creados antes de activar el cifrado se conservan y la pantalla los identifica como **sin cifrar**.

## Programar copias

En **Programación automática**, elige frecuencia y hora, selecciona la misma USB y guarda la programación. El servicio coordinador crea las copias aunque la aplicación esté cerrada. La USB debe estar conectada a la PC coordinadora a la hora programada; si no está disponible, ese intento se omite y se registra en el estado. No desconectes la memoria mientras la pantalla o el estado indique que hay una operación en curso; expúlsala desde Windows al terminar.

## Recuperar una copia en otra PC

1. Instala/actualiza Miscelánea Ari en la PC que será coordinadora y conecta la USB que contiene la copia y su ficha `.json`.
2. Entra como Administrador y abre **Más → Respaldos**.
3. Selecciona la copia cifrada y pulsa **Validar copia seleccionada**. Si esta PC aún no tiene la clave local, escribe la frase de recuperación para desbloquearla. La frase no se almacena.
4. Solo continúa si la validación indica que la copia está íntegra y es compatible con este coordinador.
5. Pulsa **Restaurar copia seleccionada**, revisa la advertencia y acepta el permiso estándar de Windows. La aplicación se cerrará mientras se pausa el coordinador.
6. Al terminar, abre de nuevo Miscelánea Ari y confirma que puedes iniciar sesión y consultar el catálogo y las operaciones.

La restauración reemplaza la base central por la copia elegida. Los cambios posteriores que no se incluyeron en ella se perderán; cualquier dispositivo cliente debe sincronizar sus operaciones pendientes antes de restaurar. El sistema conserva una copia de emergencia local por si la restauración o el inicio del coordinador fallan.

## Si algo no coincide

- **Frase incorrecta o perdida:** no se puede descifrar la copia en una PC que no conserve la clave local.
- **Copia sin ficha, modificada o incompleta:** no la restaures; conserva el archivo original y busca otra copia válida.
- **USB no detectada:** vuelve a conectarla a la PC coordinadora y pulsa **Actualizar unidades**. No cambies ni muevas los archivos de la carpeta de respaldos.
- **Respaldo antiguo `.db`:** se puede validar/restaurar con el procedimiento de la aplicación, pero no está cifrado.
- **Restauración no termina:** no borres los archivos de emergencia que aparecen en el mensaje; conserva la USB conectada y solicita asistencia antes de volver a usar el negocio.

## Actualización del coordinador

Al publicar una versión nueva, usa el instalador gráfico de Miscelánea Ari sobre el coordinador existente y elige actualizar. El asistente guarda una copia previa de la base y de los binarios. No elijas crear un negocio nuevo para actualizar una instalación existente.
