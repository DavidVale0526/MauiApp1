# Vincular Windows y Android por la red local

Este flujo prepara la autorización de dispositivos. El coordinador está instalado como Servicio de Windows y usa la base central importada desde Windows; la sincronización de cuentas y datos se incorporará después.

## Antes de usar la vinculación

1. El servicio automático debe estar activo, con la base central y HTTPS funcionando en el puerto 5080.
2. Conecta Windows y Android a la misma red del negocio. El perfil de red de Windows debe ser **Privada** para que la regla de firewall del coordinador permita conexiones entrantes. La regla del servicio solo se habilita para el perfil Privado. No se requiere Internet del proveedor.
3. Usa la dirección IPv4 privada del equipo Windows, por ejemplo `https://192.168.1.10:5080`. El puerto debe coincidir con el configurado para el servicio. Reserva la IP en el router para mantener esa dirección.
4. Copia la huella SHA-256 desde `coordinator-certificate-fingerprint.txt`, guardado junto a la base del coordinador (por defecto `C:\ProgramData\Miscelanea Ari\Coordinator`). Tiene 64 caracteres. No es el código temporal de ocho dígitos.

## Generar el código en Windows

1. Inicia una sesión local de Administrador en la aplicación y abre **Vinculación por red local**.
2. Ingresa la dirección del coordinador y su código de seguridad SHA-256.
3. Escribe las credenciales del Administrador de la base central, procedentes de la base Windows importada.
4. Pulsa **Generar código de vinculación**. El código dura cinco minutos y autoriza una sola instalación. Generar otro cancela el anterior.

## Vincular cada instalación

1. En el dispositivo, inicia sesión localmente y abre **Vinculación por red local**.
2. Ingresa la misma dirección y huella SHA-256 comprobadas en Windows.
3. Ingresa el código de ocho dígitos y pulsa **Vincular / reintentar**.
4. Confirma que la pantalla diga **Este dispositivo está vinculado**. Para vincular también Windows, genera un código para Windows y otro para Android; cada instalación conserva su propia identidad.

## Reintentar

- Si la respuesta se pierde, la clave permanece guardada de forma segura. Reintenta desde el mismo dispositivo para recuperar la confirmación.
- Si el código venció antes de que el servidor autorizara el dispositivo, genera otro en Windows y escríbelo en el formulario pendiente.
- Si la IP cambió durante una vinculación pendiente, puedes corregir la dirección manteniendo el código de seguridad del mismo coordinador.
- Si el certificado no coincide o la dirección no es válida, la aplicación rechaza la conexión. No acepta certificados automáticamente.

## Verificación real pendiente

- Revisar la pantalla en Windows y Android.
- Vincular cada instalación, cerrar/reabrir la app y comprobar que conserve su estado.
- Revisar el mensaje cuando el coordinador esté apagado y recuperar la vinculación después.
- Confirmar el flujo en el teléfono físico. Hasta entonces, las compilaciones y pruebas del cliente compartido no sustituyen esta revisión.

## Preparar el coordinador sin usar la consola

- Distribuye el paquete generado por `MauiApp1.Setup/build-windows-installer.ps1`. La persona elige **Usar esta PC como coordinador del negocio** y confirma la solicitud estándar de permisos de Windows. El asistente prepara la base vacía o importa una base existente, instala el servicio, crea el certificado HTTPS y habilita la regla de entrada solo para redes privadas. No se abre PowerShell ni se pide escribir comandos.
- En cada PC secundaria se elige **Conectar esta PC a un coordinador existente**. Después, el Administrador vincula desde **Sincronización y conexión**. Android se instala como cliente y escanea el QR generado en Windows.
- Esta PC está actualmente clasificada por Windows como perfil `Public`; por eso el firewall no acepta aún conexiones desde Android por esa Wi-Fi. Para usar el coordinador allí, confirma que es la red confiable del negocio y cambia su perfil en Configuración de Windows a `Privada` antes de emparejar dispositivos.
- No configures reenvío de puertos en el router ni expongas el coordinador a Internet.

## Actualizar una instalación existente

- Para una instalación manual, publica primero la versión que se instalará y ejecuta `MauiApp1.Coordinator/Deployment/update-coordinator.ps1` desde Windows PowerShell 5.1 o posterior como Administrador (puerto predeterminado 5080; se puede indicar otro con `-Port`). Para el flujo normal, usa el asistente gráfico de Windows: solicita los permisos de Administrador y conserva la base con un respaldo automático.
- El actualizador guarda una copia coherente SQLite de la base central y los binarios actuales antes de detener el servicio. Después reemplaza el paquete, inicia el servicio y comprueba `/health`; si falla, restaura los binarios anteriores e intenta reactivar el servicio.
- Este equipo tiene preparada la publicación y una copia preventiva, pero el servicio todavía no se ha actualizado porque la sesión de desarrollo no tiene privilegios para controlarlo. No uses las nuevas rutas de sesión hasta ejecutar el actualizador y confirmar su mensaje de éxito.
