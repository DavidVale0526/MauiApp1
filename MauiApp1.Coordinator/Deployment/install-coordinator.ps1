param(
    [Parameter(Mandatory = $true)]
    [string] $AppDirectory,

    [string] $DataDirectory = "$env:ProgramData\Miscelanea Ari\Coordinator",

    [ValidateRange(1024, 65535)]
    [int] $Port = 5080
)

$ErrorActionPreference = 'Stop'
$serviceName = 'MiscelaneaAriCoordinator'
$firewallRuleName = 'Miscelanea Ari Coordinator LAN'
$certificateFriendlyName = 'Miscelanea Ari Coordinator HTTPS'
$executablePath = [System.IO.Path]::GetFullPath((Join-Path $AppDirectory 'MauiApp1.Coordinator.exe'))
$DataDirectory = [System.IO.Path]::GetFullPath($DataDirectory)

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
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
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.RawData))
    New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
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
    Write-Output "Servicio iniciado. Puerto LAN privado: $Port"
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
