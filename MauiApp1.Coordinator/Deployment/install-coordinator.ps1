param(
    [Parameter(Mandatory = $true)]
    [string] $AppDirectory,

    [string] $DataDirectory = "$env:ProgramData\Miscelanea Ari\Coordinator",
    [string] $WindowsAppUser,
    [switch] $SetHostMode,

    [ValidateRange(1024, 65535)]
    [int] $Port = 5080
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 5) {
    throw 'Se requiere Windows PowerShell 5.1 o posterior.'
}

$serviceName = 'MiscelaneaAriCoordinator'
$firewallRuleName = 'Miscelanea Ari Coordinator LAN'
$certificateFriendlyName = 'Miscelanea Ari Coordinator HTTPS'
$executablePath = [System.IO.Path]::GetFullPath((Join-Path $AppDirectory 'MauiApp1.Coordinator.exe'))
$DataDirectory = [System.IO.Path]::GetFullPath($DataDirectory)

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$applicationUser = if ([string]::IsNullOrWhiteSpace($WindowsAppUser)) { $identity.Name } else { $WindowsAppUser }
$applicationUserSid = ([Security.Principal.NTAccount]::new($applicationUser)).Translate(
    [Security.Principal.SecurityIdentifier])
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ejecuta este script desde PowerShell como Administrador.'
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "No se encontró el ejecutable publicado: $executablePath"
}

$centralDatabasePath = Join-Path $DataDirectory 'miscelanea-ari-central.db'
if (-not (Test-Path -LiteralPath $centralDatabasePath -PathType Leaf)) {
    throw "Primero importa la base Windows aprobada con --bootstrap-from. Se esperaba: $centralDatabasePath"
}

$desktopSettingsDirectory = Join-Path $env:ProgramData 'Miscelanea Ari\Desktop'
$desktopSettingsPath = Join-Path $desktopSettingsDirectory 'windows-app-mode.json'
if (Test-Path -LiteralPath $desktopSettingsPath -PathType Leaf) {
    try { $desktopSettings = Get-Content -LiteralPath $desktopSettingsPath -Raw | ConvertFrom-Json }
    catch { throw "La configuración del modo de la aplicación Windows no es válida: $desktopSettingsPath" }
    if (-not [string]::Equals([string]$desktopSettings.Coordinator.Mode, 'Host', [StringComparison]::OrdinalIgnoreCase) -and
        -not ($SetHostMode -and [string]::Equals([string]$desktopSettings.Coordinator.Mode, 'Client', [StringComparison]::OrdinalIgnoreCase))) {
        throw "La aplicación Windows está configurada como Client. Revisa o cambia explícitamente $desktopSettingsPath antes de instalar el coordinador."
    }
    if ([string]::Equals([string]$desktopSettings.Coordinator.Mode, 'Host', [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals(
            [IO.Path]::GetFullPath([string]$desktopSettings.Coordinator.DataDirectory),
            $DataDirectory,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "La carpeta de datos del modo Host no coincide con la del servicio. Revisa $desktopSettingsPath antes de instalar."
    }
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw "El servicio $serviceName ya existe. Retíralo con el procedimiento documentado antes de instalar una versión nueva."
}

$certificate = New-SelfSignedCertificate `
    -Type SSLServerAuthentication `
    -Subject 'CN=Miscelanea Ari Coordinator' `
    -DnsName 'miscelanea-ari.local' `
    -CertStoreLocation 'Cert:\LocalMachine\My' `
    -FriendlyName $certificateFriendlyName `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy NonExportable `
    -NotAfter (Get-Date).AddYears(5)

try {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $fingerprint = ([BitConverter]::ToString($sha256.ComputeHash($certificate.RawData))).Replace('-', '')
    }
    finally { $sha256.Dispose() }
    New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
    $directoryAcl = Get-Acl -LiteralPath $DataDirectory
    $directoryRule = [Security.AccessControl.FileSystemAccessRule]::new(
        $applicationUserSid,
        [Security.AccessControl.FileSystemRights]::Modify,
        [Security.AccessControl.InheritanceFlags]::ObjectInherit,
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow)
    $directoryAcl.SetAccessRule($directoryRule)
    Set-Acl -LiteralPath $DataDirectory -AclObject $directoryAcl

    foreach ($file in @($centralDatabasePath, "$centralDatabasePath-wal", "$centralDatabasePath-shm")) {
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            $fileAcl = Get-Acl -LiteralPath $file
            $fileRule = [Security.AccessControl.FileSystemAccessRule]::new(
                $applicationUserSid,
                [Security.AccessControl.FileSystemRights]::Modify,
                [Security.AccessControl.AccessControlType]::Allow)
            $fileAcl.SetAccessRule($fileRule)
            Set-Acl -LiteralPath $file -AclObject $fileAcl
        }
    }

    Set-Content -LiteralPath (Join-Path $DataDirectory 'coordinator-certificate-fingerprint.txt') `
        -Value $fingerprint -Encoding ascii

    $url = "https://0.0.0.0:$Port"
    $arguments = '--Coordinator:Url={0} --Coordinator:DataDirectory="{1}" --Coordinator:CertificateThumbprint={2}' -f `
        $url, $DataDirectory, $certificate.Thumbprint
    $binaryPath = '"{0}" {1}' -f $executablePath, $arguments

    New-Service `
        -Name $serviceName `
        -DisplayName 'Miscelánea Ari — Coordinador LAN' `
        -Description 'API local y notificaciones SignalR para la red del negocio.' `
        -BinaryPathName $binaryPath `
        -StartupType Automatic | Out-Null

    New-NetFirewallRule `
        -DisplayName $firewallRuleName `
        -Direction Inbound `
        -Action Allow `
        -Program $executablePath `
        -Protocol TCP `
        -LocalPort $Port `
        -Profile Private | Out-Null

    Start-Service -Name $serviceName
    if (-not (Test-Path -LiteralPath $desktopSettingsPath -PathType Leaf) -or $SetHostMode) {
        New-Item -ItemType Directory -Path $desktopSettingsDirectory -Force | Out-Null
        @{ Coordinator = @{ Mode = 'Host'; DataDirectory = $DataDirectory } } |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $desktopSettingsPath -Encoding utf8
    }
    Write-Output "Servicio iniciado. Puerto LAN privado: $Port"
    Write-Output "Permisos de escritura SQLite habilitados para $applicationUser."
    Write-Output "Huella SHA-256 del certificado para vinculación: $fingerprint"
    Write-Output "Huella guardada en: $(Join-Path $DataDirectory 'coordinator-certificate-fingerprint.txt')"
}
catch {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        & sc.exe delete $serviceName | Out-Null
    }
    Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $certificate.PSPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $DataDirectory 'coordinator-certificate-fingerprint.txt') `
        -Force -ErrorAction SilentlyContinue
    throw
}
