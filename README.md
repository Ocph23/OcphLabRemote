# LabRemote

LabRemote adalah sistem TUI untuk mengelola komputer laboratorium melalui jaringan lokal. Sistem ini terdiri dari controller dan agent.

## Komponen

```text
Controller (Windows/Linux)
└── TUI: discovery, status, Wake-on-LAN, shutdown, restart

Komputer target (Windows/Linux)
└── LabRemote.Agent: HTTP API pada port TCP 5050
```

Dokumentasi lengkap:

- [Dokumentasi sistem](docs/SYSTEM-DOCUMENTATION.md)
- [Panduan instalasi](docs/INSTALLATION.md)

## Build

```powershell
dotnet build LabRemote.sln
```

## Menjalankan controller dari source

```powershell
$env:LAB_REMOTE_AGENT_TOKEN = "ganti-dengan-token-kuat"
dotnet run --project LabRemote\LabRemote.csproj
```

Controller akan mendeteksi agent pada subnet lokal melalui port `5050` dan menyimpan daftar perangkat di:

```text
%APPDATA%\LabRemote\devices.json       # Windows
~/.config/LabRemote/devices.json       # Linux
```

## Keamanan

Gunakan token acak minimal 32 byte, batasi port `5050` hanya dari IP controller, dan gunakan aplikasi hanya pada perangkat yang memang dikelola laboratorium.
