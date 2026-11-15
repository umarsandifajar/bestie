# bestie

Asisten desktop Windows yang hidup di tepi layar: jam kecil bergaya notch yang "ditarik" keluar saat disentuh kursor, lalu membuka panel berisi alat-alat harian. Ringan, tanpa akun, semua data tersimpan lokal.

![versi](https://img.shields.io/badge/versi-1.1.0-22d3ee) ![platform](https://img.shields.io/badge/Windows-10%20%7C%2011-8b5cf6) ![.NET](https://img.shields.io/badge/.NET-8-512bd4) ![lisensi](https://img.shields.io/badge/lisensi-MIT-green)

## Fitur

- **Notch jam** di tepi kiri layar dengan slider volume, mute mikrofon, dan pemutar mini (prev / play / pause / next, cover lagu, timeline).
- **Panel fisheye** dengan tab Semua, Pin, Hide, dan Prayer.
- **Prayer**: jadwal sholat (metode Kemenag RI), hitung mundur, busur matahari, pengingat adzan, Dzikir Counter. Lokasi otomatis.
- **Reminders & Alarm**: pengingat sekali bunyi atau alarm berulang, snooze, hari ulang.
- **To Do / My Task**: timeline vertikal dengan status Done, On Process, Hold.
- **Doc Converter**: gambar, DOC, DOCX, CSV ke PDF; gabung, pisah, kompres PDF.
- **Folder Organizer**: rapikan folder per jenis, tahun, bulan, atau tanggal, lengkap dengan pratinjau dan Urungkan.
- **Batch Watermark**: watermark teks atau logo untuk banyak gambar dan PDF.
- **Alat lain**: Pomodoro, Floating Timer, Notes, Clipboard History, Text Formatter, Color Picker, File Size Checker.
- Tema Black dan Terang. Library bisa ditambah sendiri: aplikasi, folder, atau link.

Panduan lengkap: [docs/PANDUAN.md](docs/PANDUAN.md)

## Unduh

Ambil file `.exe` terbaru di halaman [Releases](../../releases/latest).

| File | Keterangan |
|---|---|
| **`bestie-vX.Y.Z-setup.exe`** | **Installer (disarankan).** Tanpa perlu hak admin, ada opsi jalan otomatis saat Windows menyala dan shortcut Desktop. Tidak perlu install .NET. |
| `bestie-vX.Y.Z-win-x64.exe` | Portable: klik dua kali langsung jalan, tanpa install. |
| `bestie-vX.Y.Z-win-x64-lite.exe` | Portable kecil, butuh [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). |

### Instalasi
1. Jalankan `bestie-vX.Y.Z-setup.exe`.
2. Biarkan opsi **Jalankan bestie otomatis saat Windows menyala** tercentang (default) kalau mau otomatis jalan.
3. Selesai. bestie muncul di Start Menu dan langsung dibuka.

Mematikan jalan otomatis kapan saja: **Task Manager › Startup apps › bestie › Disable**. Uninstall lewat **Settings › Apps**; installer akan bertanya apakah data (To Do, Notes, reminder) ikut dihapus.

Windows SmartScreen bisa memunculkan peringatan karena aplikasi belum ditandatangani. Pilih **More info › Run anyway**. Checksum SHA256 ada di `SHA256SUMS.txt` pada tiap rilis.

## Persyaratan

Windows 10 (build 19041 atau lebih baru) atau Windows 11, 64-bit.

## Pengaturan awal

Instalasi pertama hanya berisi menu bawaan bestie. Aplikasi, folder, dan link tambahan ditambahkan sendiri lewat **Pengaturan › Library**. Default: tema Black, buka panel dengan klik jam, pengingat adzan aktif, Pomodoro `15,25,45,60`, Timer `5,10,15,30`.

## Build dari source

Butuh Windows dan [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Untuk installer, tambah [Inno Setup 6](https://jrsoftware.org/isdl.php).

```bat
:: jalankan langsung
dotnet run -c Release --project src/MySecretary

:: bangun installer + kedua .exe portable ke folder dist\
build.bat
```

Font judul opsional: taruh Plus Jakarta Sans (.ttf statis) di `src/MySecretary/Fonts/`. Tanpa itu, bestie memakai font terpasang atau Segoe UI.

## Rilis

Push tag `v*` memicu GitHub Actions untuk membangun installer dan kedua `.exe` portable, lalu menerbitkan Release otomatis.

```bash
git tag v1.1.0
git push origin v1.1.0
```

## Dukung

[Donate](https://sandiana.my.id)

## Lisensi

[MIT](LICENSE)
