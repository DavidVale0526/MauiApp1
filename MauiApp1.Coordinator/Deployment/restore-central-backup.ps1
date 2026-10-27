param(
    [Parameter(Mandatory = $true)] [string] $BackupPath,
    [Parameter(Mandatory = $true)] [string] $VolumeId,
    [Parameter(Mandatory = $true)] [int] $HostProcessId,
    [string] $DataDirectory = "$env:ProgramData\Miscelanea Ari\Coordinator",
    [ValidateRange(1024, 65535)] [int] $Port = 5080
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

function Show-Result([string] $Message, [string] $Title, [System.Windows.Forms.MessageBoxIcon] $Icon) {
    [System.Windows.Forms.MessageBox]::Show($Message, $Title,
        [System.Windows.Forms.MessageBoxButtons]::OK, $Icon) | Out-Null
}

function Test-CoordinatorHealth([string] $Url) {
    $curlPath = Join-Path $env:WINDIR 'System32\curl.exe'
    if (-not (Test-Path -LiteralPath $curlPath)) { return $false }
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $response = & $curlPath --insecure --fail --silent --max-time 3 $Url 2>$null
            if ($LASTEXITCODE -eq 0 -and ($response | ConvertFrom-Json).status -eq 'ok') { return $true }
        } catch { }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Invoke-DatabaseRestore([string] $Executable, [string] $FilePath, [string] $DataPath, [switch] $Emergency) {
    $restoreOption = if ($Emergency) { '--restore-emergency' } else { '--restore-central-backup' }
    $output = & $Executable $restoreOption $FilePath '--data-directory' $DataPath 2>&1
    $exitCode = $LASTEXITCODE
    $emergencyLine = $output | ForEach-Object { $_.ToString() } |
        Where-Object { $_.StartsWith('EMERGENCY_BACKUP=', [StringComparison]::Ordinal) } |
        Select-Object -Last 1
    [pscustomobject]@{
        Success = $exitCode -eq 0
        EmergencyPath = if ($emergencyLine) { $emergencyLine.Substring('EMERGENCY_BACKUP='.Length) } else { $null }
        Output = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Show-Result 'Windows no autorizó los permisos necesarios. No se modificó la base central.' `
        'Restauración cancelada' ([System.Windows.Forms.MessageBoxIcon]::Warning)
    exit 1
}

$BackupPath = [IO.Path]::GetFullPath($BackupPath)
$DataDirectory = [IO.Path]::GetFullPath($DataDirectory)
$volumeRoot = [IO.Path]::GetPathRoot($BackupPath)
$expectedBackupDirectory = [IO.Path]::GetFullPath((Join-Path $volumeRoot 'Miscelanea Ari\Respaldos'))
if (-not [string]::Equals([IO.Path]::GetDirectoryName($BackupPath), $expectedBackupDirectory,
        [StringComparison]::OrdinalIgnoreCase)) {
    Show-Result 'El archivo debe estar dentro de la carpeta Miscelanea Ari\Respaldos de una unidad local.' `
        'Ubicación no válida' ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}
$mountvolPath = Join-Path $env:WINDIR 'System32\mountvol.exe'
$mountedVolumeId = if (Test-Path -LiteralPath $mountvolPath) {
    (& $mountvolPath $volumeRoot /L 2>$null | Select-Object -First 1).TrimEnd('\')
} else { '' }
if (-not [string]::Equals($mountedVolumeId, $VolumeId.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    Show-Result 'La memoria USB seleccionada cambió o ya no está conectada. No se modificó la base central.' `
        'Memoria USB no disponible' ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}
if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath "$BackupPath.json" -PathType Leaf)) {
    Show-Result 'No se encontró el respaldo o su ficha de verificación. No se modificó la base central.' `
        'Respaldo no disponible' ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}

$serviceName = 'MiscelaneaAriCoordinator'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$serviceConfig = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
$serviceExeMatch = [regex]::Match([string]$serviceConfig.PathName, '^\s*"(?<path>[^"]+\.exe)"')
$dataMatch = [regex]::Match([string]$serviceConfig.PathName,
    '--Coordinator:DataDirectory=(?:"(?<quoted>[^"]+)"|(?<plain>\S+))', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
$serviceDataDirectory = if ($dataMatch.Groups['quoted'].Success) { $dataMatch.Groups['quoted'].Value } else { $dataMatch.Groups['plain'].Value }
if ($null -eq $service -or -not $serviceExeMatch.Success -or
    -not [string]::Equals([IO.Path]::GetFullPath($serviceDataDirectory), $DataDirectory,
        [StringComparison]::OrdinalIgnoreCase)) {
    Show-Result 'No se pudo verificar el servicio y la carpeta de la base central. No se modificó la base.' `
        'Configuración no válida' ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}
$coordinatorExecutable = $serviceExeMatch.Groups['path'].Value
if (-not (Test-Path -LiteralPath $coordinatorExecutable -PathType Leaf)) {
    Show-Result 'No se encontró el ejecutable del coordinador.' 'Restauración cancelada' `
        ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}

try {
    if ($HostProcessId -ne $PID) {
        $hostProcess = Get-Process -Id $HostProcessId -ErrorAction SilentlyContinue
        if ($null -ne $hostProcess) {
            try { $hostProcess.WaitForExit(90000) | Out-Null } catch { }
            if (Get-Process -Id $HostProcessId -ErrorAction SilentlyContinue) {
                throw 'La aplicación principal no se cerró a tiempo. La base no se modificó.'
            }
        }
    }

    $otherHostProcesses = @(Get-Process -Name 'MauiApp1' -ErrorAction SilentlyContinue)
    if ($otherHostProcesses.Count -gt 0) {
        throw 'Hay otra ventana de Miscelánea Ari abierta en esta PC. Ciérrala antes de restaurar; la base no se modificó.'
    }

    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }

    $restore = Invoke-DatabaseRestore $coordinatorExecutable $BackupPath $DataDirectory
    if (-not $restore.Success) { throw $restore.Output }
    if ([string]::IsNullOrWhiteSpace($restore.EmergencyPath) -or
        -not (Test-Path -LiteralPath $restore.EmergencyPath -PathType Leaf)) {
        throw 'La restauración no informó una copia de emergencia válida.'
    }

    Start-Service -Name $serviceName
    (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    if (-not (Test-CoordinatorHealth "https://127.0.0.1:$Port/health")) {
        throw 'El coordinador no superó la comprobación /health después de restaurar.'
    }

    Show-Result "Restauración completada y coordinador operativo.`r`n`r`nAbre de nuevo Miscelánea Ari para continuar.`r`n`r`nCopia de emergencia conservada en:`r`n$($restore.EmergencyPath)" `
        'Restauración completada' ([System.Windows.Forms.MessageBoxIcon]::Information)
    exit 0
}
catch {
    $failureMessage = $_.Exception.Message
    $emergencyRecovered = $false
    try {
        if ((Get-Service -Name $serviceName).Status -ne 'Stopped') {
            Stop-Service -Name $serviceName -Force
            (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }
        if ($restore -and $restore.EmergencyPath -and (Test-Path -LiteralPath $restore.EmergencyPath -PathType Leaf)) {
            $rollback = Invoke-DatabaseRestore $coordinatorExecutable $restore.EmergencyPath $DataDirectory -Emergency
            if (-not $rollback.Success) { throw $rollback.Output }
            $emergencyRecovered = $true
        }
        if ((Get-Service -Name $serviceName).Status -ne 'Running') {
            Start-Service -Name $serviceName
            (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        }
        if (Test-CoordinatorHealth "https://127.0.0.1:$Port/health") {
            $recoveryMessage = if ($emergencyRecovered) {
                'Se recuperó la copia de emergencia y el coordinador responde correctamente.'
            } else {
                'La base central no se reemplazó y el coordinador responde correctamente.'
            }
            Show-Result "No se pudo completar la restauración solicitada.`r`n$failureMessage`r`n`r`n$recoveryMessage" `
                'Restauración no completada' ([System.Windows.Forms.MessageBoxIcon]::Warning)
            exit 1
        }
        throw 'El coordinador no respondió después de recuperar la copia de emergencia.'
    }
    catch {
        Show-Result "Falló la restauración y la recuperación automática requiere atención.`r`n`r`nError: $failureMessage`r`nRecuperación: $($_.Exception.Message)`r`n`r`nNo borres las copias de emergencia. Solicita asistencia técnica antes de iniciar la aplicación." `
            'Se requiere recuperación manual' ([System.Windows.Forms.MessageBoxIcon]::Error)
        exit 2
    }
}
