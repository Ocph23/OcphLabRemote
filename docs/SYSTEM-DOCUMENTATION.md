# Dokumentasi Sistem LabRemote

## 1. Tujuan

LabRemote digunakan untuk mengelola komputer laboratorium secara terpusat dari terminal. Operasi yang tersedia adalah:

- mendeteksi agent pada jaringan lokal;
- menampilkan status online/offline secara realtime;
- mengelompokkan perangkat berdasarkan group/lab;
- menambahkan perangkat secara manual;
- menghidupkan komputer melalui Wake-on-LAN;
- shutdown dan restart melalui agent.

Sistem ini ditujukan untuk jaringan laboratorium yang dikelola oleh administrator.

## 2. Arsitektur

```text
+----------------------+       HTTP TCP 5050       +----------------------+
| Controller            | ------------------------> | LabRemote.Agent      |
| LabRemote TUI         |                           | Komputer target      |
| Windows/Linux         |       Wake-on-LAN         | Windows/Linux        |
+----------+-----------+ ------------------------> +----------------------+
           |
           +---- UDP broadcast port 7/9
```

Controller menjalankan discovery dengan melakukan ping ke alamat IP host-host di subnet lokal, lalu mengambil MAC address dan hostname. Controller juga mencoba `GET /health` pada port `5050` untuk membedakan host yang menjalankan `LabRemote.Agent`.

## 3. Fitur controller

### 3.0 Alur tampilan

Saat controller dijalankan, aplikasi menampilkan splash screen:

```text
SMKN 8 TIK KOTA JAYAPURA
```

Setelah splash selesai, aplikasi membuka halaman group. Operator dapat memilih `Discover`, menambahkan group, atau membuka group. Setelah group dipilih, aplikasi membuka halaman daftar device khusus group tersebut. Pada halaman device, `Enter` membuka context menu device dengan aksi `Wake`, `Launch`, `Shutdown`, `Restart`, dan `Remove`. `Launch` hanya aktif ketika device berstatus `Online`. Menu utama halaman device berisi `Back`, `Add Device`, `Wake All`, `Launch All`, `Shutdown All`, `Restart All`, dan `Refresh`.

Tombol `Esc` dapat digunakan sebagai pintasan `Back` pada halaman dan `Batal` pada dialog atau context menu.

Pada halaman Discover dan halaman device, tekan `Space` untuk menandai beberapa device. Baris yang sedang dipilih ditampilkan dengan highlight kuning. Tombol `Move group` memindahkan semua device yang ditandai, sedangkan `Remove selected` menghapus semua device yang ditandai setelah konfirmasi. Pada halaman device, tombol `Delete` juga dapat digunakan untuk menghapus pilihan.

### 3.1 Daftar device

Daftar device ditampilkan otomatis saat aplikasi dibuka. Informasi yang ditampilkan meliputi:

- group/lab;
- nama komputer;
- alamat IP;
- status;
- waktu status terakhir diterima.

### 3.2 Group

Device baru dapat diberi group seperti `Lab 1`, `Lab 2`, atau `Server`. Gunakan tombol `Add group` untuk membuat group baru, `Remove group` untuk menghapus group kosong, dan `Move group` untuk memindahkan device yang sedang dipilih, termasuk device dari group `Discovered`.

Gunakan `Remove` pada context menu untuk menghapus device dari `devices.json`. Penghapusan memerlukan konfirmasi dan tidak menghapus group.

Group dan device disimpan bersama dalam `devices.json`, sehingga perubahan tetap ada saat controller dijalankan kembali.

### 3.3 Status realtime

Controller memanggil endpoint `/health` setiap lima detik. Status yang ditampilkan:

- `Online`: agent merespons HTTP 200;
- `Offline`: koneksi timeout atau agent tidak merespons;
- `Error`: agent merespons dengan status HTTP selain 2xx;
- `Unknown`: belum ada pemeriksaan status.

### 3.4 Discovery

Tombol `Discover` memindai host aktif dalam subnet IPv4 lokal. Untuk setiap host, controller mencoba mengambil MAC address melalui `SendARP` pada Windows atau tabel neighbor `ip neigh` pada Linux, kemudian melakukan resolusi hostname dan pemeriksaan agent pada port TCP `5050`.

Discovery tidak dapat menemukan komputer yang sedang mati. Komputer yang sudah pernah diketahui tetap dapat dibangunkan melalui MAC address menggunakan Wake-on-LAN.

### 3.5 Wake-on-LAN

Controller mengirim magic packet ke:

- directed broadcast subnet, misalnya `192.168.1.255`;
- broadcast umum `255.255.255.255`;
- UDP port `7` dan `9`;
- tiga kali pengulangan.

Implementasi default mengasumsikan subnet `/24`. BIOS/UEFI, kartu jaringan, switch, dan sistem operasi target harus mendukung Wake-on-LAN.

### 3.6 Shutdown dan restart

Controller mengirim:

```text
POST /power/shutdown
POST /power/restart
```

Agent menjalankan perintah platform:

```text
Windows shutdown /s /t 5
Windows shutdown /r /t 5
Linux   systemctl poweroff
Linux   systemctl reboot
```

## 4. API agent

### Health check

```http
GET /health
```

Contoh respons:

```json
{
  "status": "online",
  "machine": "PC-LAB-001",
  "operatingSystem": "Linux ...",
  "macAddress": "AABBCCDDEEFF",
  "utc": "2026-10-17T10:00:00+00:00"
}
```

### Power command

Header wajib:

```http
X-Agent-Token: token-rahasia
```

Contoh:

```http
POST /power/shutdown
POST /power/restart
```

Respons yang mungkin:

- `202 Accepted`: perintah diterima;
- `400 Bad Request`: action tidak valid;
- `401 Unauthorized`: token salah atau tidak ada;
- `500`: perintah sistem gagal dijalankan.

## 5. Penyimpanan konfigurasi

Controller menyimpan device secara lokal dalam `devices.json`. Contoh:

```json
{
  "Devices": [
    {
      "Name": "PC-LAB-001",
      "IpAddress": "192.168.1.100",
      "MacAddress": "74-27-EA-2C-13-51",
      "Group": "Lab 1",
      "Status": "Online",
      "LastAction": "-"
    }
  ]
}
```

File ini dapat dicadangkan, tetapi token tidak disimpan di dalamnya. Token dibaca dari environment variable `LAB_REMOTE_AGENT_TOKEN`.

## 6. Batasan

- Discovery hanya menemukan host aktif pada subnet lokal yang dapat dijangkau; host tanpa agent tetap dapat tampil dengan status offline.
- Komputer mati tidak dapat ditemukan melalui `/health`.
- Wake-on-LAN tidak bekerja jika komputer kehilangan daya, NIC tidak mendukung WoL, atau broadcast diblokir jaringan.
- Implementasi directed broadcast saat ini mengasumsikan subnet `/24`.
- Port agent menggunakan HTTP dalam jaringan lokal; untuk jaringan yang tidak sepenuhnya dipercaya, gunakan VPN atau HTTPS.

## 7. Rekomendasi operasional

- Gunakan VLAN khusus laboratorium jika memungkinkan.
- Izinkan TCP `5050` hanya dari IP controller.
- Gunakan token unik untuk lingkungan produksi.
- Simpan backup `devices.json`.
- Uji shutdown pada satu komputer sebelum operasi massal.
- Beri konfirmasi operator sebelum shutdown/restart massal.
