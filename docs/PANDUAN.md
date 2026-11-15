# Panduan penggunaan bestie v1.1.0

Panduan lengkap semua fitur. Untuk instalasi dan build, lihat [README](../README.md).

## Instalasi & jalan otomatis
Pakai `bestie-vX.Y.Z-setup.exe`. Centang **Jalankan bestie otomatis saat Windows menyala** untuk auto-start. Ubah kapan saja di Task Manager › Startup apps › bestie. Versi portable (`...-win-x64.exe`) tidak membuat auto-start.

Config tersimpan di `%APPDATA%\MySecretary\config.json` (config lama dari RadialLauncher otomatis dimigrasi).

## Pengaturan awal (instalasi pertama)
- Tema **Black**, panel dibuka dengan **Klik jam**.
- "Buka panel saat kursor menyentuh tepi kiri layar" **mati**.
- Pengingat waktu adzan **aktif**.
- Pilihan Pomodoro `15,25,45,60` menit; pilihan Timer `5,10,15,30` menit; durasi awal Timer `5` menit.
- Library **kosong**: hanya menu bawaan bestie. Aplikasi, folder, dan link ditambahkan sendiri lewat **Pengaturan › Library** atau tombol **+**.
- Lokasi jadwal sholat mengikuti koneksi internet (cadangan offline: Jakarta).

## Cara pakai
- **Jam kecil** menempel di tepi kiri layar (jam di baris 1, menit di baris 2). Sentuh dengan kursor → notch "ditarik" keluar, memanjang ke bawah, dan muncul kontrol: **slider volume** (gradien cyan-violet; geser / klik / roda mouse; ikon speaker di dasarnya = mute/unmute audio), **tombol mute mikrofon**, dan tombol **pengaturan (gear)** yang menyembul dari dasar notch. **Klik angka jam** → panel meluncur keluar.
- Alternatif: kursor menempel lama di tepi kiri (di luar area jam) juga membuka panel. Jauhkan kursor → panel menutup sendiri.
- Daftar item bergaya **fisheye**: item aktif di tengah (ukuran penuh), item di atas/bawahnya mengecil dan memudar bertahap, tanpa scroll bar. Geser dengan roda mouse atau ↑ ↓; klik item samping untuk membawanya ke tengah, klik item tengah (atau Enter) untuk menjalankan. Ikon pin/mata muncul di item tengah.
- Tab: **Semua · Pin · Hide · Prayer**.
- Header panel: logo **bestie** ("bes" putih, "tie" gradien cyan-violet) dan tombol **Donate** (buka sandiana.my.id). **Esc** menutup panel.
- Saat ada audio dari komputer, muncul visualizer putih kecil di atas angka jam; arahkan kursor ke sana untuk balon lagu: latar cover lagu (hitam bila tidak ada), garis timeline cyan-violet, dan tombol **prev · play/pause · next** yang bisa diklik. Balon tetap ada selama kursor di visualizer atau di balon; setelah dijeda lewat balon, ia bertahan ~1 menit supaya bisa dilanjutkan.
- Hover ikon → gradien cyan-violet. Jam dan panel membal halus saat disentuh kursor.
- **Prayer** (tombol cyan-violet di sebelah Hide): jadwal sholat + hitung mundur + busur matahari animasi (tanpa scroll bar: isi mengecil proporsional mengikuti tinggi panel), panah ‹ › untuk melihat hari lain. Lokasi mengikuti koneksi internet (disimpan untuk mode offline), metode Kemenag RI. Saat waktu adzan tiba muncul pop-up "Waktu Adzan … telah tiba" + bunyi bel lembut; bisa dimatikan/dites di Pengaturan › Umum. Dzikir Counter senyap saat diklik; bunyi hanya saat hitungan selesai.
- Footer: ⚙ = pengaturan / keluar · **Library** = kelola aplikasi, folder, link · **+** = tambah cepat.
- **Tema**: Black (default) atau Terang — di ⚙ atau Pengaturan › Umum.
- Jendela alat: tombol **X** di kanan atas (merah saat disentuh), atau **Esc**.
- **Doc Converter**: gambar (JPG/PNG/WebP) → PDF, **DOC / DOCX / CSV → PDF**, gabung PDF, pisah PDF (tiap halaman / per rentang), kompres gambar & PDF. Tarik-lepas file ke jendela, atur urutan dengan ↑ ↓. Hasil disimpan di folder sumber (atau folder output pilihan), nama file tidak menimpa yang lama. Kompres PDF merender ulang halaman jadi gambar (teks tidak bisa diseleksi lagi).
  - DOC/DOCX: dikonversi lewat Microsoft Word bila terpasang, kalau tidak LibreOffice, kalau tidak pembaca DOCX bawaan (teks, judul, tabel; gambar tidak ikut). File .doc lama butuh Word atau LibreOffice.
  - CSV: jadi tabel PDF (pembatas , ; tab | terdeteksi otomatis), header diulang tiap halaman, orientasi otomatis / potret / lanskap. CSV dengan kolom sangat banyak dibagi ke beberapa halaman.
- **Folder Organizer**: rapikan file satu folder ke sub-folder per jenis dan/atau **per tahun / bulan / tanggal** (toggle, pratinjau muncul sebelum dijalankan; tanggal pakai "terakhir diubah" atau "dibuat"). Setelah **Rapikan sekarang** muncul hasilnya: file dipindah ke folder mana, nama foldernya, jumlah file. **Warnai folder (stabilo)** mengganti ikon folder jadi berwarna per jenis; warnanya bisa diganti lewat titik warna di hasil. Tombol **Urungkan** mengembalikan file, menghapus folder yang dibuat, dan menghapus warna. Warna disimpan sebagai `desktop.ini` + `.bestie-color.ico` tersembunyi di dalam folder (folder yang sudah punya `desktop.ini` sendiri tidak ditimpa).
- **Notes** (dulu "Catatan cepat"): catatan cepat yang tersimpan otomatis. Pin / hide / recent lama ikut pindah ke nama baru.
- **Reminders & Alarm**: dua jenis. **Alarm** = pop-up lonceng + bunyi bel berulang sampai ditunda atau dimatikan. **Reminder** = pop-up lonceng + satu bunyi bel lembut (seperti pengingat adzan), hilang sendiri setelah 30 detik. Keduanya punya tombol **Tunda** (snooze 5 / 10 / 15 menit, diatur per item) dan **Matikan / Oke**. Atur jam lewat spinner (panah ±5 menit, roda mouse ±1), tombol cepat (+5 mnt, +15 mnt, +30 mnt, +1 jam), dan pilih hari ulang (kosong = sekali saja). Klik item di daftar untuk mengedit, toggle untuk menyalakan/mematikan, ✕ untuk menghapus. **Tes pop-up** untuk melihat tampilan dan bunyinya. Jadwal tetap jalan walau panel bestie tertutup (selama bestie aktif); alarm yang terlewat saat bestie mati tidak dibunyikan.
- **To Do / My Task**: daftar tugas bergaya **timeline vertikal** (titik status di kiri, garis penghubung, tanggal kecil, judul tebal, keterangan abu-abu). **Klik titik status** untuk memilih **Done** (terisi gradien + centang, judul dicoret), **On Process** (cincin + jam) atau **Hold** (abu-abu + jeda); pilihan **Hapus** ada di menu yang sama. Arahkan kursor ke baris → tombol ✕ untuk menghapus; ikon tempat sampah di bar atas = **hapus semua yang sudah Done** supaya daftar tidak penuh. Klik teks tugas untuk mengubah judul / keterangan (Esc = batal). Tambah tugas lewat kolom di bawah (Enter = simpan; kolom kedua = keterangan opsional).
  - Kotak bisa **digeser** (tarik bar atas), **di-resize** (tarik tepi atau sudut mana pun), dan punya ikon **pin = selalu di atas** (bisa dimatikan). Posisi, ukuran, dan pengaturan pin diingat; semua tugas tersimpan di config.
  - Saat kotak **ditutup** dan masih ada tugas yang belum Done, muncul **ikon To Do (ungu)** di notch, tepat di atas slider volume, dengan angka jumlah tugas aktif. Klik ikon → kotak muncul lagi. Bila pin "selalu di atas" dimatikan, ikon tetap tampil supaya kotak yang tertutup jendela lain mudah ditemukan.
- **Ikon lonceng di notch**: bila ada **alarm** aktif muncul lonceng **kuning**, bila ada **pengingat** aktif muncul lonceng **cyan** (angka kecil bila lebih dari satu). Klik lonceng → panel Reminders & Alarm terbuka. Ikon hilang sendiri saat tidak ada lagi yang aktif (alarm sekali-pakai yang sudah berbunyi, dimatikan, atau dihapus).
- **Batch Watermark**: tempel watermark **teks** (6 pilihan warna) atau **logo** (PNG transparan paling bagus) ke banyak **gambar (JPG/PNG/WebP/BMP) dan PDF** sekaligus. Atur opasitas, ukuran (% lebar), rotasi, dan posisi (9 titik atau **Ubin** berulang diagonal). Ada pratinjau langsung (memakai file pertama di daftar, atau contoh bila kosong). Hasil jadi file baru bernama `nama-wm.ext` di folder sumber (atau folder output pilihan) dan tidak menimpa apa pun. Format gambar hasil bisa Sama / JPG / PNG / WebP. Di PDF, watermark ditempel sebagai gambar transparan di tiap halaman (teks PDF aslinya tetap bisa diseleksi). PDF berpassword tidak didukung. Pengaturan terakhir diingat.
- Pomodoro: sisa waktu tampil di barisnya.
- Floating Timer: garis cyan-violet memendek mengikuti sisa waktu. Pilihan durasi & durasi awal diatur di Pengaturan › Umum.
