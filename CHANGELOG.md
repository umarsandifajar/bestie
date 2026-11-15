# Changelog

## [1.1.0] - 2026-11-15

Rilis publik pertama.

### Default instalasi baru
- Library kosong: tidak ada aplikasi atau folder bawaan, hanya menu bawaan bestie.
- Buka panel dari tepi kiri layar: mati.
- Pilihan durasi Timer: `5,10,15,30` menit.
- Tema Black, buka panel dengan klik jam, pengingat adzan aktif, Pomodoro `15,25,45,60`.

### Distribusi
- Installer `bestie-v1.1.0-setup.exe` (Inno Setup): tanpa admin, opsi jalan otomatis saat Windows menyala, shortcut Start Menu / Desktop, uninstall bersih dengan pilihan hapus data.
- Build `.exe` portable standalone dan versi lite (butuh .NET 8 Desktop Runtime).
- Rilis otomatis lewat GitHub Actions, lengkap dengan checksum SHA256.

Pengguna lama: config yang sudah ada di `%APPDATA%\MySecretary\config.json` tidak diubah.
