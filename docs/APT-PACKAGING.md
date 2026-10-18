# Instalasi melalui APT di Debian

Paket Debian yang disediakan adalah `ocphlabremote-agent`. Paket ini memasang agent, membuat token acak, memasang service systemd, dan menjalankan agent pada port TCP `5050`.

## Membuat paket

Jalankan di Debian, Ubuntu, atau WSL Debian/Ubuntu:

```bash
sudo apt update
sudo apt install -y dotnet-sdk-8.0 dpkg-dev openssl
cd /path/ke/ocph23-wol
bash packaging/build-agent-deb.sh 0.1.0
```

Untuk server ARM64:

```bash
DEB_ARCH=arm64 DOTNET_RID=linux-arm64 bash packaging/build-agent-deb.sh 0.1.0
```

## Instalasi paket lokal

```bash
sudo apt install ./artifacts/deb/ocphlabremote-agent_0.1.0_amd64.deb
```

APT akan memasang dependensi, menjalankan post-install script, membuat token acak di `/etc/labremote-agent.env`, dan mengaktifkan service.

## Verifikasi

```bash
sudo systemctl status ocphlabremote-agent
curl http://127.0.0.1:5050/health
sudo ss -tulpn | grep ':5050'
```

## Firewall

Batasi akses ke IP controller:

```bash
sudo ufw allow from IP_CONTROLLER to any port 5050 proto tcp
```

## Token

Token dibuat otomatis ketika paket pertama kali dipasang:

```bash
sudo cat /etc/labremote-agent.env
```

Gunakan token tersebut pada controller melalui environment variable `LAB_REMOTE_AGENT_TOKEN`.

## Repository APT internal

Agar dapat menggunakan:

```bash
sudo apt install ocphlabremote-agent
```

tanpa path `./`, file `.deb` harus dipublikasikan ke repository APT internal atau repository HTTPS. Instalasi `.deb` lokal tetap menggunakan:

```bash
sudo apt install ./ocphlabremote-agent_0.1.0_amd64.deb
```

## GitHub Pages

Workflow `.github/workflows/publish-apt.yml` membangun dan menerbitkan repository APT ketika tag `v*` dibuat.

Siapkan repository GitHub:

1. Buat GPG key khusus repository APT.
2. Simpan private key ASCII-armored sebagai repository secret `APT_GPG_PRIVATE_KEY`.
3. Aktifkan GitHub Pages dengan source `GitHub Actions`.
4. Push tag versi:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Setelah workflow selesai, pasang public key pada Debian:

```bash
curl -fsSL https://USERNAME.github.io/REPOSITORY/ocphlabremote-archive-keyring.gpg \
  | sudo tee /usr/share/keyrings/ocphlabremote.gpg >/dev/null
```

Tambahkan repository dan instal paket:

```bash
echo "deb [signed-by=/usr/share/keyrings/ocphlabremote.gpg] https://USERNAME.github.io/REPOSITORY stable main" \
  | sudo tee /etc/apt/sources.list.d/ocphlabremote.list
sudo apt update
sudo apt install ocphlabremote-agent
```
