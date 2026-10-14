# Modo de la aplicación Windows

Una instalación normal de la aplicación Windows funciona como cliente. No hay que ejecutar comandos para configurarla como PC cliente. La aplicación tampoco decide su rol por encontrar un archivo SQLite en `ProgramData`.

Para una instalación nueva, usa el asistente gráfico `MiscelaneaAriSetup.exe`: selecciona **Cliente** en las PC secundarias o **Coordinador** en la única PC Windows que alojará el servicio. La ruta Coordinador usa la confirmación de permisos estándar de Windows y deja instalada la base, el servicio y el modo Host. El instalador gráfico se genera con `MauiApp1.Setup/build-windows-installer.ps1`.

La configuración se guarda en:

`C:\ProgramData\Miscelanea Ari\Desktop\windows-app-mode.json`

El archivo es opcional para los clientes. Si existe, contiene `Coordinator.Mode` con el valor `Host` o `Client`. En `Host` también contiene `Coordinator.DataDirectory`, que debe ser el mismo directorio usado por el servicio coordinador.

## CoordinadorPC

Al instalar el servicio coordinador, `install-coordinator.ps1` registra automáticamente el modo `Host`. Al actualizar el servicio, `update-coordinator.ps1` verifica que el modo y la carpeta de datos coincidan; si la configuración todavía no existía, la crea como `Host` usando la carpeta que recibió el actualizador.

## PC2 u otra PC cliente

Abre la aplicación y usa la vista **Sincronización y conexión** para vincularla con el coordinador. Si esta PC conserva una base antigua en `ProgramData`, la aplicación la ignora y usa su base local; no la borra.

Solo hace falta intervenir la configuración si esa PC ya fue instalada expresamente como coordinador. En ese caso, el cambio de rol es una tarea de mantenimiento y debe revisarse con quien administra la instalación; no es un paso del uso normal.

## Mantenimiento del CoordinadorPC

La configuración manual queda reservada para reparar la instalación del coordinador desde PowerShell como Administrador:

```powershell
& "<ruta del proyecto>\MauiApp1\Deployment\configure-windows-app-mode.ps1" -Mode Host -DataDirectory "C:\ProgramData\Miscelanea Ari\Coordinator"
```

Si no existe el archivo de modo, la aplicación arranca como cliente. Si existe una configuración explícita inválida, la aplicación se detiene y muestra el error; nunca abre una base central basándose solo en encontrar un archivo SQLite.
