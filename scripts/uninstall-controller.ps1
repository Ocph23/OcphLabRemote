[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\OCPHLabRemote\Controller",
    [switch]$RemoveToken
)

$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Jalankan PowerShell sebagai Administrator."
}


Remove-Item $InstallDirectory -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "OCPH LabRemote Agent berhasil dihapus."
