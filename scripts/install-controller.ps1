[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$InstallDirectory = "$env:ProgramFiles\OCPHLabRemote\Controller"
)

$ErrorActionPreference = "Stop"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Jalankan PowerShell sebagai Administrator."
}

$owner = "Ocph23"
$repository = "OcphLabRemote"
$assetName = "ocphlabremote-controller-win-x64.zip"

if ($Version -eq "latest") {
    $release = Invoke-RestMethod "https://api.github.com/repos/$owner/$repository/releases/latest"
    $Version = $release.tag_name
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("ocphlabremote-controller-" + [guid]::NewGuid())
$zipPath = Join-Path $tempRoot $assetName
$extractPath = Join-Path $tempRoot "extracted"
$downloadUrl = "https://github.com/$owner/$repository/releases/download/$Version/$assetName"

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    Write-Host "Mengunduh controller $Version..."
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath
    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force
    Copy-Item (Join-Path $extractPath "*") $InstallDirectory -Recurse -Force
    Write-Host "Controller terpasang di $InstallDirectory"
    Write-Host "Jalankan: $InstallDirectory\LabRemote.exe"
    Start-Process (Join-Path $InstallDirectory "LabRemote.exe")
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
