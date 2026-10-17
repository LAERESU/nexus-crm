# E5 · Dashboard

Tujuan epic: halaman pertama setelah login menampilkan ringkasan kerja user.
Referensi utama: `ARCHITECTURE.md` B.3 (`/summary`), E.4.

---

### NX-041 · Endpoint ringkasan dashboard
- **Sprint:** 5 · **SP:** 3 · **Bergantung pada:** NX-033, NX-040
- **Langkah:**
  1. Task Service: `GET /tasks/stats/me` (jumlah per status, jatuh tempo 7 hari, terlambat).
  2. Project Service: `GET /projects/stats/me` (proyek aktif yang diikuti).
  3. Cache 1 menit per user. Frontend menggabungkan kedua endpoint (tidak membuat agregator di backend pada tahap ini).
- **Acceptance criteria:**
  - [ ] Angka cocok dengan data di halaman Tugas saya dan daftar proyek.
  - [ ] Respons < 300 ms saat cache hit.

---

### NX-042 · Kartu statistik + grafik
- **Sprint:** 5 · **SP:** 5 · **Bergantung pada:** NX-041
- **Langkah:** `DashboardPage` dengan `StatCard` (proyek aktif, jatuh tempo minggu ini, terlambat), grafik batang tugas per status (Recharts), widget daftar tugas terdekat.
- **Acceptance criteria:**
  - [ ] Klik kartu "Terlambat" membuka halaman Tugas saya dengan filter terlambat.
  - [ ] Tampilan rapi di lebar layar 1280 px dan 768 px.

---

### NX-043 · Widget SOP menunggu approval & SOP baru
- **Sprint:** 8 · **SP:** 3 · **Bergantung pada:** NX-051, NX-053
- **Langkah:** tambahkan kartu "SOP butuh approval" (untuk approver) dan "SOP baru belum dibaca" (untuk semua user) dari endpoint SOP Service.
- **Acceptance criteria:**
  - [ ] Widget hanya tampil untuk user dengan permission terkait.
