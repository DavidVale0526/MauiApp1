param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Host', 'Client')]
    [string] $Mode,

    [string] $DataDirectory = "$env:ProgramData\Miscelanea Ari\Coordinator",

    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ejecuta PowerShell como Administrador para configurar el modo de esta instalación de Windows.'
}

$settingsDirectory = Join-Path $env:ProgramData 'Miscelanea Ari\Desktop'
$settingsPath = Join-Path $settingsDirectory 'windows-app-mode.json'
$configuration = @{ Coordinator = @{ Mode = $Mode } }

if ($Mode -eq 'Host') {
    $DataDirectory = [IO.Path]::GetFullPath($DataDirectory)
    $centralDatabasePath = Join-Path $DataDirectory 'miscelanea-ari-central.db'
    if (-not (Test-Path -LiteralPath $centralDatabasePath -PathType Leaf)) {
        throw "No se encontró la base central. No se cambió el modo. Se esperaba: $centralDatabasePath"
    }

    $serviceKey = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\MiscelaneaAriCoordinator' -ErrorAction SilentlyContinue
    $serviceImagePath = [string]$serviceKey.ImagePath
    $serviceDataMatch = [regex]::Match(
        $serviceImagePath,
        '--Coordinator:DataDirectory=(?:"(?<quoted>[^"]+)"|(?<plain>\S+))',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $serviceDataDirectory = if ($serviceDataMatch.Groups['quoted'].Success) {
        $serviceDataMatch.Groups['quoted'].Value
    } elseif ($serviceDataMatch.Groups['plain'].Success) {
        $serviceDataMatch.Groups['plain'].Value
    } else { $null }
    if ([string]::IsNullOrWhiteSpace($serviceDataDirectory) -or
        -not [string]::Equals(
            [IO.Path]::GetFullPath($serviceDataDirectory),
            $DataDirectory,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "La carpeta indicada no coincide con la que usa el servicio MiscelaneaAriCoordinator. No se cambió el modo. Servicio: $serviceImagePath"
    }

    $configuration.Coordinator.DataDirectory = $DataDirectory
}

if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
    try { $existing = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json }
    catch { throw "La configuración existente no es válida: $settingsPath. No se modificó." }

    $sameMode = [string]::Equals([string]$existing.Coordinator.Mode, $Mode, [StringComparison]::OrdinalIgnoreCase)
    $sameDirectory = $Mode -eq 'Client' -or
        [string]::Equals(
            [IO.Path]::GetFullPath([string]$existing.Coordinator.DataDirectory),
            $DataDirectory,
            [StringComparison]::OrdinalIgnoreCase)
    if ($sameMode -and $sameDirectory) {
        Write-Output "El modo ya está configurado como ${Mode}: $settingsPath"
        return
    }
    if (-not $Force) {
        throw "Ya existe una configuración distinta en $settingsPath. Revisa el modo y usa -Force solo si confirmas el cambio de rol."
    }

    $backupPath = "$settingsPath.Backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item -LiteralPath $settingsPath -Destination $backupPath
    Write-Output "Configuración anterior respaldada en: $backupPath"
}

New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
$configuration | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $settingsPath -Encoding utf8
Write-Output "Modo de la aplicación Windows configurado como ${Mode}: $settingsPath"
Write-Output 'Cierra y vuelve a abrir la aplicación para que tome el modo configurado.'
