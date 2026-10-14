param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\windows-installer'),
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$bundleDirectory = Join-Path $outputRoot "MiscelaneaAri-$stamp"
$stageDirectory = Join-Path $bundleDirectory 'Payload'
$appDirectory = Join-Path $stageDirectory 'App'
$coordinatorDirectory = Join-Path $stageDirectory 'Coordinator'
$scriptsDirectory = Join-Path $stageDirectory 'Scripts'
$setupDirectory = Join-Path $outputRoot "MiscelaneaAri-SetupBuild-$stamp"
$restoreArguments = @()
if ($NoRestore) { $restoreArguments += '--no-restore' }

New-Item -ItemType Directory -Path $appDirectory, $coordinatorDirectory, $scriptsDirectory, $setupDirectory -Force | Out-Null

dotnet publish (Join-Path $repositoryRoot 'MauiApp1\MauiApp1.csproj') `
    --configuration Release --framework net10.0-windows10.0.19041.0 --runtime win-x64 `
    --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true `
    -p:NuGetAudit=false $restoreArguments --output $appDirectory -m:1
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar la aplicación Windows.' }

dotnet publish (Join-Path $repositoryRoot 'MauiApp1.Coordinator\MauiApp1.Coordinator.csproj') `
    --configuration Release --framework net10.0 --runtime win-x64 --self-contained true `
    -p:NuGetAudit=false $restoreArguments --output $coordinatorDirectory -m:1
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el servicio coordinador.' }

dotnet publish (Join-Path $PSScriptRoot 'MauiApp1.Setup.csproj') `
    --configuration Release --framework net10.0-windows --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:NuGetAudit=false $restoreArguments `
    --output $setupDirectory -m:1
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el instalador gráfico.' }
Get-ChildItem -LiteralPath $setupDirectory -File | Copy-Item -Destination $bundleDirectory -Force

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'MauiApp1.Coordinator\Deployment\install-coordinator.ps1') `
    -Destination $scriptsDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'MauiApp1.Coordinator\Deployment\update-coordinator.ps1') `
    -Destination $scriptsDirectory
$readme = @'
MISCELÁNEA ARI — INSTALADOR PARA WINDOWS

1. Extrae la carpeta completa del instalador.
2. Ejecuta MiscelaneaAriSetup.exe.
3. Elige una sola PC como coordinadora. Las otras PC y los teléfonos se instalan como clientes.
4. Para la coordinadora, Windows mostrará su solicitud normal de permisos. El asistente instalará el servicio y preparará la red sin abrir una consola.
5. En los clientes, instala la app y después usa Sincronización y conexión para vincularlos.

El coordinador puede iniciar un negocio nuevo con una base vacía o importar una base existente de Miscelánea Ari. La base central nunca se sobrescribe.
Si ejecutas el asistente en una PC que ya tiene el coordinador, ofrece actualizarlo desde la interfaz. Antes de cambiar los binarios, crea una copia de la base y de los binarios actuales.
'@
Set-Content -LiteralPath (Join-Path $bundleDirectory 'LEEME.txt') -Value $readme -Encoding utf8

$zipPath = "$bundleDirectory.zip"
Compress-Archive -Path (Join-Path $bundleDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Output "Instalador gráfico listo: $zipPath"
