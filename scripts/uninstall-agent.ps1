[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\OCPHLabRemote\Agent",
    [switch]$RemoveToken
)

$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Jalankan PowerShell sebagai Administrator."
}

$serviceName = "ocphlabremote-agent"
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}

Remove-NetFirewallRule -DisplayName "OCPH LabRemote Agent TCP 5050" -ErrorAction SilentlyContinue
Remove-Item $InstallDirectory -Recurse -Force -ErrorAction SilentlyContinue
if ($RemoveToken) {
    [Environment]::SetEnvironmentVariable("LAB_REMOTE_AGENT_TOKEN", $null, "Machine")
}
Write-Host "OCPH LabRemote Agent berhasil dihapus."
