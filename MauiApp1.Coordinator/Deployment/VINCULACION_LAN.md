# Vincular Windows y Android por la red local

Este flujo prepara la autorización de dispositivos. La instalación del servicio, importación real de Windows y respaldo de Android siguen pendientes en T7.1. La sincronización de cuentas y datos se incorporará después.

## Antes de usar la vinculación

1. El coordinador debe estar instalado, con la base inicial aprobada y HTTPS funcionando.
2. Conecta Windows y Android a la misma red del negocio. La red puede funcionar aunque el proveedor de Internet esté caído.
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
