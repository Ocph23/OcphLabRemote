[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$InstallDirectory = "$env:ProgramFiles\OCPHLabRemote\Agent"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Jalankan PowerShell sebagai Administrator."
}

$owner = "Ocph23"
$repository = "OcphLabRemote"
$serviceName = "ocphlabremote-agent"
$assetName = "ocphlabremote-agent-win-x64.zip"

if ($Version -eq "latest") {
    $release = Invoke-RestMethod "https://api.github.com/repos/$owner/$repository/releases/latest"
    $Version = $release.tag_name
}

$downloadUrl = "https://github.com/$owner/$repository/releases/download/$Version/$assetName"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("ocphlabremote-" + [guid]::NewGuid())
$zipPath = Join-Path $tempRoot $assetName
$extractPath = Join-Path $tempRoot "extracted"

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    Write-Host "Mengunduh agent $Version..."
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath

    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    }

    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force
    Copy-Item (Join-Path $extractPath "*") $InstallDirectory -Recurse -Force

    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }
    $token = ([Convert]::ToHexString($bytes)).ToLowerInvariant()

    if (-not [Environment]::GetEnvironmentVariable("LAB_REMOTE_AGENT_TOKEN", "Machine")) {
        [Environment]::SetEnvironmentVariable("LAB_REMOTE_AGENT_TOKEN", $token, "Machine")
        Write-Host "Token baru dibuat dan disimpan sebagai environment variable Machine."
    } else {
        Write-Host "Token lama dipertahankan."
    }

    $exePath = Join-Path $InstallDirectory "LabRemote.Agent.exe"
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        sc.exe delete $serviceName | Out-Null
        Start-Sleep -Seconds 1
    }

    New-Service -Name $serviceName -BinaryPathName "`"$exePath`"" -DisplayName "OCPH LabRemote Agent" `
        -Description "Agent remote power control untuk laboratorium" -StartupType Automatic | Out-Null

    if (-not (Get-NetFirewallRule -DisplayName "OCPH LabRemote Agent TCP 5050" -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName "OCPH LabRemote Agent TCP 5050" -Direction Inbound `
            -Protocol TCP -LocalPort 5050 -Action Allow -Profile Domain,Private | Out-Null
    }

    Start-Service -Name $serviceName
    Write-Host "Agent berhasil dipasang dan dijalankan."
    Write-Host "Health: http://127.0.0.1:5050/health"
    Write-Host "Token:  $([Environment]::GetEnvironmentVariable('LAB_REMOTE_AGENT_TOKEN', 'Machine'))"
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
