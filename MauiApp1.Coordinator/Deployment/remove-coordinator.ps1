$ErrorActionPreference = 'Stop'
$serviceName = 'MiscelaneaAriCoordinator'
$firewallRuleName = 'Miscelanea Ari Coordinator LAN'
$certificateFriendlyName = 'Miscelanea Ari Coordinator HTTPS'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ejecuta este script desde PowerShell como Administrador.'
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete $serviceName | Out-Null
}

Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction SilentlyContinue

Get-ChildItem -Path 'Cert:\LocalMachine\My' |
    Where-Object FriendlyName -eq $certificateFriendlyName |
    Remove-Item -Force

Write-Output 'Servicio, regla de firewall y certificado retirados. La base central y sus respaldos se conservaron.'
