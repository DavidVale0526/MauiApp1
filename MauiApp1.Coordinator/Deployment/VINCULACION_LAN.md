# Vincular Windows y Android por la red local

Este flujo autoriza dispositivos para conectarse al coordinador LAN. El coordinador está instalado como Servicio de Windows y usa la base central del negocio. Vincular un dispositivo no crea una cuenta ni inicia sesión: después del vínculo, la persona inicia sesión con su cuenta central para sincronizar.

## Antes de usar la vinculación

1. El servicio automático debe estar activo, con la base central y HTTPS funcionando en el puerto configurado (5080 por defecto).
2. Conecta Windows y los dispositivos a la misma red privada del negocio. El perfil de red de Windows debe ser **Privada**. El instalador habilita HTTPS del coordinador por TCP y descubrimiento mDNS por UDP 5353, ambos limitados al perfil Privado; mDNS también se restringe a la subred local. No se requiere Internet del proveedor.
3. Si el router o punto de acceso bloquea multicast, o activa aislamiento entre clientes, la búsqueda puede no encontrar el coordinador. Usa QR o configuración manual como alternativa inmediata. No abras el coordinador a Internet ni configures reenvío de puertos.

## Descubrir y solicitar el vínculo

1. En Android o en una PC Windows instalada como **Cliente**, abre **Sincronización y conexión** y luego **Vincular dispositivo**.
2. Pulsa **Buscar coordinador en esta red**, elige el coordinador del negocio y pulsa **Solicitar vínculo seguro**.
3. Compara el código de ocho dígitos que aparece en ese dispositivo con el que aparece en la sección de solicitudes de Windows. La solicitud vence en tres minutos.
4. En el coordinador, inicia sesión en la aplicación como Administrador. En **Vinculación por red local**, actualiza las solicitudes y aprueba solo después de comprobar que el código coincide. Si no coincide, rechaza la solicitud.
5. El dispositivo completa el vínculo al recibir la aprobación. Después debe iniciar sesión con una cuenta central; la vinculación por sí sola no crea usuarios ni sincroniza.

La búsqueda usa DNS-SD/mDNS para localizar el servicio. El anuncio solo contiene el nombre público, versión, puerto e identificador derivado del certificado; no contiene cuentas, contraseñas, códigos, tokens ni datos del negocio. La aplicación valida el coordinador por HTTPS y vincula el código de comparación a una negociación criptográfica efímera. La red y el nombre anunciado no se consideran prueba de confianza.

Si Windows está configurado con el perfil **Público**, el firewall no permitirá el acceso LAN. Confirma en Configuración de Windows que es la red confiable del negocio y cámbiala a **Privada**; la aplicación no cambia ese ajuste automáticamente.

## Alternativa: QR o configuración manual

La búsqueda no reemplaza las formas existentes. Si mDNS no está disponible, puede usarse el QR o ingresarse la dirección y huella manualmente. Para esta alternativa, usa la dirección IPv4 privada del coordinador, por ejemplo `https://192.168.1.10:5080`, y la huella SHA-256 guardada en `coordinator-certificate-fingerprint.txt`, normalmente dentro de `C:\ProgramData\Miscelanea Ari\Coordinator`. La huella tiene 64 caracteres y no es el código temporal de ocho dígitos.

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
- En cada PC secundaria se elige **Conectar esta PC a un coordinador existente**. Después, el dispositivo puede descubrir el coordinador desde **Sincronización y conexión** y solicitar aprobación; QR y conexión manual siguen disponibles. Android se instala como cliente.
- Si Windows clasifica la red como `Public`, el firewall no acepta conexiones entrantes desde los otros dispositivos. Confirma que sea una red confiable y cambia su perfil en Configuración de Windows a `Private` antes de vincular.
- No configures reenvío de puertos en el router ni expongas el coordinador a Internet.

## Actualizar una instalación existente

- Para una instalación manual, publica primero la versión que se instalará y ejecuta `MauiApp1.Coordinator/Deployment/update-coordinator.ps1` desde Windows PowerShell 5.1 o posterior como Administrador (puerto predeterminado 5080; se puede indicar otro con `-Port`). Para el flujo normal, usa el asistente gráfico de Windows: solicita los permisos de Administrador y conserva la base con un respaldo automático.
- El actualizador guarda una copia coherente SQLite de la base central y los binarios actuales antes de detener el servicio. Después reemplaza el paquete, inicia el servicio y comprueba `/health`; si falla, restaura los binarios anteriores e intenta reactivar el servicio.
- Después de publicar una versión nueva, usa el asistente gráfico para actualizar el servicio con respaldo previo. El instalador añade o conserva la regla mDNS de UDP 5353 solo para la subred local y el perfil Privado. No uses la búsqueda automática hasta actualizar el coordinador y comprobar que el servicio inicia correctamente.
