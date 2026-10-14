param(
    [string] $AppDirectory = "$env:ProgramFiles\Miscelanea Ari\Coordinator",
    [string] $PublishDirectory = (Join-Path $PSScriptRoot '..\bin\publish\win-x64'),
    [string] $DataDirectory = "$env:ProgramData\Miscelanea Ari\Coordinator",
    [string] $WindowsAppUser,
    [ValidateRange(1024, 65535)] [int] $Port = 5080
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'Ejecuta este actualizador con PowerShell 7 o posterior.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ejecuta PowerShell como Administrador para actualizar el servicio.'
}

$serviceName = 'MiscelaneaAriCoordinator'
$appDirectory = [IO.Path]::GetFullPath($AppDirectory)
$publishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
$dataDirectory = [IO.Path]::GetFullPath($DataDirectory)
$serviceExecutable = Join-Path $appDirectory 'MauiApp1.Coordinator.exe'
$publishExecutable = Join-Path $publishDirectory 'MauiApp1.Coordinator.exe'
$databasePath = Join-Path $dataDirectory 'miscelanea-ari-central.db'
$desktopSettingsDirectory = Join-Path $env:ProgramData 'Miscelanea Ari\Desktop'
$desktopSettingsPath = Join-Path $desktopSettingsDirectory 'windows-app-mode.json'
$windowsIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ([string]::IsNullOrWhiteSpace($WindowsAppUser)) { $WindowsAppUser = $windowsIdentity.Name }
$windowsAppSid = ([Security.Principal.NTAccount]::new($WindowsAppUser)).Translate(
    [Security.Principal.SecurityIdentifier])

function Grant-CoordinatorDatabaseAccess {
    param([string] $DatabaseDirectory, [string] $DatabaseFile, [Security.Principal.SecurityIdentifier] $UserSid)

    # SQLite may write to the database directory and to existing -wal/-shm files.
    # Grant only the Windows desktop account that runs the MAUI app, not all Users.
    $directoryAcl = Get-Acl -LiteralPath $DatabaseDirectory
    $directoryRule = [Security.AccessControl.FileSystemAccessRule]::new(
        $UserSid,
        [Security.AccessControl.FileSystemRights]::Modify,
        [Security.AccessControl.InheritanceFlags]::ObjectInherit,
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow)
    $directoryAcl.SetAccessRule($directoryRule)
    Set-Acl -LiteralPath $DatabaseDirectory -AclObject $directoryAcl

    foreach ($file in @($DatabaseFile, "$DatabaseFile-wal", "$DatabaseFile-shm")) {
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            $fileAcl = Get-Acl -LiteralPath $file
            $fileRule = [Security.AccessControl.FileSystemAccessRule]::new(
                $UserSid,
                [Security.AccessControl.FileSystemRights]::Modify,
                [Security.AccessControl.AccessControlType]::Allow)
            $fileAcl.SetAccessRule($fileRule)
            Set-Acl -LiteralPath $file -AclObject $fileAcl
        }
    }
}

if (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
    throw "No se encontró el servicio $serviceName."
}
$serviceConfiguration = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'"
$serviceImagePath = [string]$serviceConfiguration.PathName
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
        $dataDirectory,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "La ruta -DataDirectory no coincide con la configuración registrada del servicio. El servicio no se detuvo. Servicio: $serviceImagePath"
}

foreach ($path in @($serviceExecutable, $publishExecutable, $databasePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Falta un archivo requerido: $path"
    }
}

if (Test-Path -LiteralPath $desktopSettingsPath -PathType Leaf) {
    try { $desktopSettings = Get-Content -LiteralPath $desktopSettingsPath -Raw | ConvertFrom-Json }
    catch { throw "La configuración del modo de la aplicación Windows no es válida: $desktopSettingsPath. El servicio no se detuvo." }
    if (-not [string]::Equals([string]$desktopSettings.Coordinator.Mode, 'Host', [StringComparison]::OrdinalIgnoreCase)) {
        throw "La aplicación Windows está configurada como Client. El actualizador no cambiará su modo: $desktopSettingsPath"
    }
    if (-not [string]::Equals(
            [IO.Path]::GetFullPath([string]$desktopSettings.Coordinator.DataDirectory),
            $dataDirectory,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "La carpeta central configurada para la aplicación no coincide con la del servicio. El servicio no se detuvo: $desktopSettingsPath"
    }
}
foreach ($runtimeFile in @('hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll')) {
    $runtimePath = Join-Path $publishDirectory $runtimeFile
    if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) {
        throw "La publicación debe ser independiente del runtime e incluir $runtimeFile. Publica con -r win-x64 --self-contained true. El servicio no se detuvo."
    }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path (Join-Path $dataDirectory 'Backups') "PreUpdate-$stamp"
$databaseBackupDirectory = Join-Path $backupRoot 'Database'
$binaryBackupDirectory = Join-Path $backupRoot 'CoordinatorBin'
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

# The coordinator bootstrap uses SQLite Backup API, so this remains consistent with WAL mode.
$bootstrap = Start-Process -FilePath $publishExecutable -ArgumentList @(
    '--bootstrap-from', "`"$databasePath`"", '--data-directory', "`"$databaseBackupDirectory`""
) -Wait -PassThru -NoNewWindow
if ($bootstrap.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $databaseBackupDirectory 'miscelanea-ari-central.db'))) {
    throw 'No se pudo crear y validar el respaldo de la base central; el servicio no se detuvo.'
}

New-Item -ItemType Directory -Path $binaryBackupDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $appDirectory '*') -Destination $binaryBackupDirectory -Recurse -Force
if (-not (Test-Path -LiteralPath (Join-Path $binaryBackupDirectory 'MauiApp1.Coordinator.exe'))) {
    throw 'No se pudo respaldar el binario actual; el servicio no se detuvo.'
}

$serviceStopped = $false
try {
    Stop-Service -Name $serviceName
    (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    $serviceStopped = $true
    Grant-CoordinatorDatabaseAccess -DatabaseDirectory $dataDirectory -DatabaseFile $databasePath -UserSid $windowsAppSid
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $appDirectory -Recurse -Force
    Start-Service -Name $serviceName
    (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))

    $health = $null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri "https://127.0.0.1:$Port/health" -SkipCertificateCheck -TimeoutSec 5
            if ($health.status -eq 'ok') { break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if ($health.status -ne 'ok') { throw 'El coordinador no respondió correctamente en /health.' }

    if (-not (Test-Path -LiteralPath $desktopSettingsPath -PathType Leaf)) {
        # Running the coordinator-specific updater on this machine explicitly establishes Host mode.
        New-Item -ItemType Directory -Path $desktopSettingsDirectory -Force | Out-Null
        @{ Coordinator = @{ Mode = 'Host'; DataDirectory = $dataDirectory } } |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $desktopSettingsPath -Encoding utf8
        Write-Output "Modo Host de la aplicación Windows registrado en: $desktopSettingsPath"
    }

    Write-Output "Permisos de escritura SQLite habilitados para $WindowsAppUser."
    Write-Output "Actualización correcta. Respaldo de base y binarios: $backupRoot"
}
catch {
    if ($serviceStopped) {
        try {
            Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
            Copy-Item -Path (Join-Path $binaryBackupDirectory '*') -Destination $appDirectory -Recurse -Force
            Start-Service -Name $serviceName -ErrorAction SilentlyContinue
        } catch { }
    }
    throw
}
