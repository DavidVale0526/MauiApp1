# Instalador gráfico de Windows

El instalador ofrece dos rutas: **Cliente** y **Coordinador**. Una PC cliente instala la app en el perfil de la persona y se vincula desde **Sincronización y conexión**. Android siempre se instala como cliente.

La ruta Coordinador pide la elevación estándar de Windows. El asistente crea una base central vacía o importa una base SQLite seleccionada, instala el servicio HTTPS, habilita el puerto solo en redes privadas, genera el certificado local y deja el modo `Host` configurado. La aplicación no sobrescribe una base central existente ni instala un segundo servicio sobre el coordinador ya registrado.

Si la PC ya tiene el servicio coordinador instalado, el asistente reconoce ese equipo y ofrece actualizarlo desde la interfaz. Antes de sustituir los binarios, genera un respaldo de la base y de los binarios actuales; conserva la ubicación y los datos centrales. El usuario solo confirma el aviso normal de permisos de Windows.

## Crear el paquete de distribución

Ejecuta `build-windows-installer.ps1` en el entorno de desarrollo con el SDK de .NET y la carga de trabajo MAUI para Windows instalados. Genera un ZIP que contiene el asistente gráfico y sus cargas útiles autocontenidas. El usuario final extrae el ZIP y abre `MiscelaneaAriSetup.exe`; no necesita abrir una consola. Android se instala como cliente con el paquete Android de la aplicación y se vincula desde su pantalla de sincronización.

## Inicio del negocio

En una instalación nueva, la base central se prepara antes de abrir la app. La primera vez que se abre la aplicación en la PC coordinadora, se puede crear allí la cuenta Administrador. Para trasladar datos existentes, el instalador permite elegir la base y el coordinador la copia usando SQLite Backup API sin modificar el origen.
