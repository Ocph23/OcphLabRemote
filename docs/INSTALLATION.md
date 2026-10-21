# Panduan Instalasi LabRemote

## 1. Prasyarat

- .NET 8 SDK untuk build dari source;
- jaringan lokal yang sama atau routing antar-subnet;
- port TCP `5050` dari controller ke agent;
- UDP broadcast port `7` atau `9` untuk Wake-on-LAN;
- token rahasia yang sama pada controller dan semua agent.

Generate token kuat dari PowerShell:

```powershell
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
([BitConverter]::ToString($bytes) -replace '-','').ToLowerInvariant()
```

## 2. Build dan publish

### Controller Windows

```powershell
dotnet publish .\LabRemote\LabRemote.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\publish-controller
```

### Controller Linux x64

```bash
dotnet publish ./LabRemote/LabRemote.csproj \
  -c Release -r linux-x64 --self-contained true \
  -o ./publish-controller
```

### Agent Windows x64

```powershell
dotnet publish .\LabRemote.Agent\LabRemote.Agent.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\publish-agent
```

### Agent Debian/Ubuntu x64

```powershell
dotnet publish .\LabRemote.Agent\LabRemote.Agent.csproj `
  -c Release -r linux-x64 --self-contained true `
  -o .\publish-agent
```

Untuk server ARM64 gunakan `linux-arm64`.

## 3. Instalasi agent di Debian/Linux

### Opsi A — instalasi melalui APT

Cara yang direkomendasikan untuk server Debian/Ubuntu adalah menggunakan repository APT:

```bash
sudo apt update
sudo apt install -y ca-certificates curl gnupg
curl -fsSL https://ocph23.github.io/OcphLabRemote/ocphlabremote-archive-keyring.gpg \
  | sudo tee /usr/share/keyrings/ocphlabremote.gpg >/dev/null
echo "deb [signed-by=/usr/share/keyrings/ocphlabremote.gpg] https://ocph23.github.io/OcphLabRemote stable main" \
  | sudo tee /etc/apt/sources.list.d/ocphlabremote.list
sudo apt update
sudo apt install ocphlabremote-agent
```

Paket akan membuat token acak dan service `ocphlabremote-agent` secara otomatis.

Verifikasi instalasi:

```bash
sudo systemctl status ocphlabremote-agent
curl http://127.0.0.1:5050/health
sudo ss -tulpn | grep ':5050'
sudo cat /etc/labremote-agent.env
```

### Opsi B — instalasi manual dari hasil publish

Salin hasil publish:

```bash
scp -r publish-agent user@IP_SERVER:/tmp/labremote-agent
```

Di Debian:

```bash
sudo mkdir -p /opt/labremote-agent
sudo cp -r /tmp/labremote-agent/* /opt/labremote-agent/
sudo chmod +x /opt/labremote-agent/LabRemote.Agent
```

Buat secret file:

```bash
sudo install -m 600 /dev/null /etc/labremote-agent.env
sudo nano /etc/labremote-agent.env
```

Isi:

```env
LAB_REMOTE_AGENT_TOKEN=GANTI_DENGAN_TOKEN_KUAT
```

Buat service:

```bash
sudo nano /etc/systemd/system/labremote-agent.service
```

Isi:

```ini
[Unit]
Description=LabRemote Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=root
WorkingDirectory=/opt/labremote-agent
EnvironmentFile=/etc/labremote-agent.env
ExecStart=/opt/labremote-agent/LabRemote.Agent
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Aktifkan:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now labremote-agent
sudo systemctl status labremote-agent
```

Konfigurasi appsettings dapat menggunakan token kosong karena token produksi dibaca dari `EnvironmentFile`:

```json
{
  "Urls": "http://0.0.0.0:5050",
  "Agent": { "Token": "" }
}
```

Firewall UFW, batasi ke IP controller:

```bash
sudo ufw allow from IP_CONTROLLER to any port 5050 proto tcp
sudo ufw status
```

Uji:

```bash
curl http://127.0.0.1:5050/health
sudo ss -tulpn | grep ':5050'
```

## 4. Instalasi agent di Windows

Set token sebagai environment variable sistem:

```powershell
[Environment]::SetEnvironmentVariable(
  "LAB_REMOTE_AGENT_TOKEN",
  "GANTI_DENGAN_TOKEN_KUAT",
  "Machine"
)
```

### OPSI A - Instalasi Cepat

Pastikan PowerShell 7 sudah terpasang. Jika belum, pasang melalui winget:

```
winget install --id Microsoft.Powershell --source winget
```

Panggil PowerShell 

```powershell
pwsh
```

Buka PowerShell sebagai Administrator, lalu jalankan:

```powershell
irm https://raw.githubusercontent.com/Ocph23/OcphLabRemote/master/scripts/install-agent.ps1 | iex
```

Script akan mengunduh release Windows terbaru, memasang agent ke:

```text
C:\Program Files\OCPHLabRemote\Agent
```

Script juga membuat token machine, Windows Service `ocphlabremote-agent`, dan rule firewall TCP `5050` untuk profil Domain/Private.

Verifikasi:

```powershell
Get-Service ocphlabremote-agent
Invoke-WebRequest http://127.0.0.1:5050/health
[Environment]::GetEnvironmentVariable("LAB_REMOTE_AGENT_TOKEN", "Machine")
```

Untuk memasang versi tertentu:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/Ocph23/OcphLabRemote/master/scripts/install-agent.ps1))) -Version v0.1.8
```

Untuk menghapus agent:

```powershell
irm https://raw.githubusercontent.com/Ocph23/OcphLabRemote/master/scripts/uninstall-agent.ps1 | iex
```

Tambahkan `-RemoveToken` jika token machine juga ingin dihapus.


To open a port in Windows using the terminal, use Windows Terminal or Command Prompt as Administrator. You can open a port using Windows Firewall (netsh).

1. Open a port using CMD
For example, to allow incoming TCP traffic on port 5050:


```cmd
netsh advfirewall firewall add rule name="Open Port 5050" dir=in action=allow protocol=TCP localport=5050
For UDP instead:
```


```cmd
netsh advfirewall firewall add rule name="Open Port 5050 UDP" dir=in action=allow protocol=UDP localport=5050
```

2. Open a port using PowerShell
Run PowerShell as Administrator:

```powershell
New-NetFirewallRule -DisplayName "Open Port 5050" -Direction Inbound -Protocol TCP -LocalPort 5050 -Action Allow
```

### OPSI B - Instalasi agent manual di Windows

Download : https://github.com/Ocph23/OcphLabRemote/releases
```
ocphlabremote-agent-win-x64.zip
```

Buka PowerShell sebagai Administrator. Salin folder publish ke:

```text
C:\Program Files\OCPHLabRemote\Agent
```

Set token sebagai environment variable sistem:

```powershell
[Environment]::SetEnvironmentVariable(
  "LAB_REMOTE_AGENT_TOKEN",
  "GANTI_DENGAN_TOKEN_KUAT",
  "Machine"
)
```

Buat rule firewall:

```powershell
New-NetFirewallRule `
  -DisplayName "LabRemote Agent 5050" `
  -Direction Inbound `
  -Protocol TCP `
  -LocalPort 5050 `
  -Action Allow `
  -Profile Domain,Private
```

Daftarkan sebagai Windows Service:

```powershell
New-Service `
  -Name "ocphlabremote-agent" `
  -DisplayName "Ocph LabRemote Agent" `
  -Description "Ocph LabRemote control agent" `
  -BinaryPathName "C:\Program Files\OCPHLabRemote\Agent\LabRemote.Agent.exe" `
  -StartupType Automatic
```

Jalankan:

```powershell
Start-Service ocphlabremote-agent
Get-Service ocphlabremote-agent
```

Uji lokal:

```powershell
Invoke-WebRequest http://127.0.0.1:5050/health
Get-NetTCPConnection -LocalPort 5050 -State Listen
```


#### Hapus Service

Stop the service
```
Stop-Service -Name "ocphlabremote-agent" -Force
```
atau 
```
sc.exe stop "ocphlabremote-agent"
```



Remove the service
```
Remove-Service -Name "ocphlabremote-agent"
```
atau 
```
sc.exe delete "ocphlabremote-agent"
```


## 5. Konfigurasi Wake dan Launch

Agent hanya menjalankan aplikasi yang terdaftar pada allowlist `Agent:AllowedApplications`.

Contoh Windows di `appsettings.json`:

```json
"AllowedApplications": {
  "notepad": {
    "Executable": "C:\\Windows\\System32\\notepad.exe",
    "Arguments": ""
  },
  "seb-ipdn": {
    "Executable": "C:\\Program Files\\SafeExamBrowser\\Seb.exe",
    "Arguments": "\"D:\\Downloads\\SEB-CAT-IPDN-0824.seb\""
  },
  "chrome-ipdn": {
    "Executable": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "Arguments": "--new-window \"https://example.com\""
  }
}
```

Contoh Linux:

```json
"AllowedApplications": {
  "browser": "/usr/bin/firefox"
}
```

Setelah mengubah konfigurasi Linux:

```bash
sudo systemctl restart ocphlabremote-agent
```

Pada TUI, pilih device lalu tekan `Enter`. Gunakan `Wake` untuk menyalakan perangkat. Setelah status berubah menjadi `Online`, pilih `Launch` dan masukkan alias aplikasi.

Pada Windows, agent menggunakan sesi user yang sedang login agar aplikasi GUI tampil di desktop user, bukan hanya berjalan di sesi Windows Service.

Restart Service Agent
```
Restart-Service ocphlabremote-agent
```


## 6. Instalasi controller

### Windows

```powershell
$env:LAB_REMOTE_AGENT_TOKEN = "GANTI_DENGAN_TOKEN_KUAT"
cd C:\LabRemote\publish-controller
.\LabRemote.exe
```

### Linux

```bash
export LAB_REMOTE_AGENT_TOKEN='GANTI_DENGAN_TOKEN_KUAT'
cd /opt/labremote-controller
./LabRemote
```

Controller akan menampilkan seluruh device yang tersimpan dan menjalankan discovery otomatis pada startup. Gunakan tombol `Discover` untuk memindai ulang.

### Instalasi cepat controller Windows

Buka PowerShell sebagai Administrator, lalu jalankan:

```powershell
irm https://raw.githubusercontent.com/Ocph23/OcphLabRemote/master/scripts/install-controller.ps1 | iex
```

Controller dipasang ke `C:\Program Files\OCPHLabRemote\Controller` dan langsung dijalankan.

### Instalasi cepat controller Linux x64

```bash
curl -fsSL https://raw.githubusercontent.com/Ocph23/OcphLabRemote/master/scripts/install-controller.sh \
  | bash
export LAB_REMOTE_AGENT_TOKEN='TOKEN_YANG_SAMA_DENGAN_AGENT'
ocphlabremote
```

Untuk versi tertentu, berikan tag release sebagai argumen:

```bash
curl -fsSL https://raw.githubusercontent.com/Ocph23/OcphLabRemote/master/scripts/install-controller.sh \
  | bash -s -- v0.1.13
```

### Uninstall agent Windows

Buka PowerShell sebagai Administrator:

```powershell
irm https://raw.githubusercontent.com/Ocph23/OcphLabRemote/master/scripts/uninstall-agent.ps1 | iex
```

Perintah tersebut menghentikan dan menghapus service, menghapus rule firewall, serta menghapus file agent. Untuk menghapus token machine juga:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/Ocph23/OcphLabRemote/master/scripts/uninstall-agent.ps1))) -RemoveToken
```

### Uninstall agent Debian melalui APT

```bash
sudo systemctl stop ocphlabremote-agent
sudo apt purge ocphlabremote-agent
sudo rm -f /etc/apt/sources.list.d/ocphlabremote.list
sudo rm -f /usr/share/keyrings/ocphlabremote.gpg
sudo apt update
```

`apt purge` menghapus paket dan service. File token `/etc/labremote-agent.env` dapat dihapus manual jika masih tersisa:

```bash
sudo rm -f /etc/labremote-agent.env
```

### Uninstall agent Debian instalasi manual

```bash
sudo systemctl disable --now labremote-agent
sudo rm -f /etc/systemd/system/labremote-agent.service
sudo systemctl daemon-reload
sudo rm -rf /opt/labremote-agent
sudo rm -f /etc/labremote-agent.env
```

## 7. Pengujian tanpa shutdown

Uji health dari controller:

```powershell
Invoke-WebRequest http://IP_AGENT:5050/health
```

Uji token tanpa mematikan komputer:

```powershell
Invoke-WebRequest `
  -Method Post `
  -Uri "http://IP_AGENT:5050/power/test" `
  -Headers @{ "X-Agent-Token" = "TOKEN_YANG_SAMA" }
```

Respons `400 Bad Request` menunjukkan token diterima dan action `test` ditolak karena memang bukan action valid. Respons `401 Unauthorized` menunjukkan token tidak sama.

## 8. Troubleshooting

### Service Linux gagal `203/EXEC`

```bash
ls -l /opt/labremote-agent/LabRemote.Agent
file /opt/labremote-agent/LabRemote.Agent
sudo chmod +x /opt/labremote-agent/LabRemote.Agent
sudo journalctl -u labremote-agent -n 50 --no-pager
```

Pastikan publish sesuai arsitektur server: `linux-x64` untuk `x86_64`, atau `linux-arm64` untuk `aarch64`.

### Status selalu Offline

```bash
sudo ss -tulpn | grep ':5050'
sudo systemctl status labremote-agent
```

Periksa firewall dan IP address. Agent harus listen pada `0.0.0.0:5050`, bukan hanya `127.0.0.1`.

### Status 401 Unauthorized

Pastikan token controller dan agent sama. Pada Linux periksa service:

```bash
sudo systemctl cat labremote-agent
sudo systemctl daemon-reload
sudo systemctl restart labremote-agent
```

### Wake-on-LAN gagal

Periksa MAC address, aktifkan WoL di BIOS/UEFI, gunakan kabel LAN, dan pastikan switch meneruskan broadcast. Uji terlebih dahulu dengan satu komputer yang sudah diketahui mendukung WoL.
