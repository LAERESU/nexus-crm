# E3 · Project Management

Tujuan epic: tim bisa membuat proyek, mengelola anggota dan milestone; event proyek tersebar ke service lain.
Referensi utama: `ARCHITECTURE.md` A.2, A.3, B.3, C.2 (PRJ_SCHEMA), D.4–D.6.

---

### NX-025 · Schema & CRUD proyek
- **Sprint:** 4 · **SP:** 8 · **Bergantung pada:** NX-020
- **Langkah:**
  1. Migrasi `db/prj/V2__projects.sql`: `PROJECTS` + index `IX_PROJECTS_TNT_STATUS`.
  2. Domain `Project` (status `PLANNING/ACTIVE/ON_HOLD/COMPLETED/ARCHIVED`, validasi tanggal mulai ≤ selesai).
  3. Command/query: create, update (dengan `If-Match` → `ROW_VERSION`), archive (soft delete), get, list (filter status, owner, departemen, search, paging).
  4. Pembuat proyek otomatis menjadi anggota dengan peran `MANAGER`.
- **Acceptance criteria:**
  - [x] Update dengan `ROW_VERSION` usang → 409.
  - [x] Proyek yang diarsipkan tidak muncul di list default.
  - [x] Unit test domain + integration test endpoint lulus.
- **Catatan:** arsip adalah status `ARCHIVED` (bukan `IS_DELETED`): `DELETE /projects/{id}` mengarsipkan, dan arsip tampil dengan `?status=archived`. Tabel `PROJECT_MEMBERS` dibuat di sini karena langkah 4 membutuhkannya; endpoint anggota dan pemeriksaan keanggotaan tetap di NX-026. `OWNER_ID`/`DEPARTMENT_ID` menyimpan ID publik dari User Service tanpa divalidasi ke sana. Permission tulis diperiksa terhadap scope penugasan role (global, proyek itu, atau departemennya); pemeriksaan keanggotaan untuk membaca tetap di NX-026. Arsip belum bisa dibatalkan dan proyek arsip tetap memegang kodenya. Kode `ExpectRowVersion`/audit/outbox di `ProjectDbContext` masih salinan dari User Service; pemindahan ke BuildingBlocks belum dikerjakan. Event proyek tersimpan di outbox; pengirimnya ke RabbitMQ dipasang di NX-028.

---

### NX-026 · Anggota proyek & peran proyek
- **Sprint:** 4 · **SP:** 5 · **Bergantung pada:** NX-025
- **Langkah:**
  1. Tabel `PROJECT_MEMBERS` dan read model `USER_REF` (diisi dari event `UserCreated/Updated/Deactivated`).
  2. Endpoint tambah/hapus/ubah peran anggota; hanya `MANAGER` proyek atau admin tenant yang boleh.
  3. Pemeriksaan kepemilikan objek: hanya anggota yang bisa membaca proyek (kecuali admin/auditor).
- **Acceptance criteria:**
  - [x] Bukan anggota → 403 saat membuka detail proyek.
  - [x] Manager terakhir tidak bisa dihapus dari proyek.
- **Catatan:** "admin/auditor" dikenali lewat permission baru `project.all.read`; mengubah proyek dan anggota butuh peran `MANAGER` di proyek itu (atau `project.all.read`). User Service kini mempublikasikan `UserUpdated` dan `UserDeactivated`. User yang belum ada di `USER_REF` tetap bisa dijadikan anggota (nama kosong sampai event-nya tiba). Lihat ADR-0011.

---

### NX-027 · Milestone proyek
- **Sprint:** 4 · **SP:** 3 · **Bergantung pada:** NX-025
- **Langkah:** tabel `MILESTONES`, endpoint list/create/update, validasi tanggal di dalam rentang proyek.
- **Acceptance criteria:**
  - [x] Milestone di luar rentang tanggal proyek ditolak (422).
- **Catatan:** status milestone `open`/`completed`; batas rentang yang kosong tidak membatasi. Rentang tanggal proyek juga tidak bisa diubah sampai mengecualikan milestone yang ada (422). Membaca butuh keanggotaan proyek; membuat/mengubah butuh `project.update` dan peran MANAGER (ADR-0011). Membuat milestone atau mengubah tanggalnya menaikkan versi proyek, sehingga perubahan rentang proyek yang bersamaan bentrok (409). Menghapus milestone belum ada (tidak termasuk langkah story).

---

### NX-028 · Outbox pattern + publish event
- **Sprint:** 4 · **SP:** 5 · **Bergantung pada:** NX-009, NX-025
- **Langkah:**
  1. Di BuildingBlocks.Messaging: simpan domain event ke `OUTBOX_MESSAGES` dalam transaksi `SaveChangesAsync` (ARCHITECTURE D.4).
  2. `OutboxPublisherWorker` (BackgroundService) membaca outbox tiap 2 detik dengan `FOR UPDATE SKIP LOCKED`, publish via MassTransit, tandai `PROCESSED_AT`.
  3. Consumer base class yang mencatat `PROCESSED_EVENTS` agar idempotent.
  4. Publish `ProjectCreated`, `ProjectUpdated`, `MemberAdded`, `MemberRemoved`, `ProjectArchived`.
  5. Terapkan juga ke User Service (event NX-018/019).
- **Acceptance criteria:**
  - [ ] Jika RabbitMQ mati saat proyek dibuat, data tetap tersimpan dan event terkirim setelah RabbitMQ hidup.
  - [ ] Event yang sama dikonsumsi dua kali tidak menghasilkan efek ganda.

---

### NX-029 · Cache proyek & anggota
- **Sprint:** 4 · **SP:** 3 · **Bergantung pada:** NX-026
- **Langkah:** cache-aside untuk `tnt:{t}:project:{id}` (10 menit) dan `tnt:{t}:project:{id}:members`; hapus key di setiap command yang mengubah data.
- **Acceptance criteria:**
  - [ ] Request detail kedua dilayani dari cache (terlihat di log/metrik).
  - [ ] Perubahan data langsung terlihat (cache terhapus).

---

### NX-030 · Halaman daftar proyek + form
- **Sprint:** 4 · **SP:** 5 · **Bergantung pada:** NX-024, NX-025
- **Langkah:** `features/projects`: `ProjectListPage` (tabel, filter, search, paging), `ProjectForm` di drawer (create/edit), aksi arsip dengan konfirmasi.
- **Acceptance criteria:**
  - [ ] Membuat proyek baru langsung muncul di daftar tanpa reload (invalidasi query).
  - [ ] Konflik 409 menampilkan pesan "data telah diubah orang lain" dan memuat ulang data.

---

### NX-031 · Halaman detail proyek
- **Sprint:** 4 · **SP:** 5 · **Bergantung pada:** NX-026, NX-027, NX-030
- **Langkah:** `ProjectDetailPage` dengan tab Ringkasan, Anggota (`MemberPicker`), Milestone (`MilestoneTimeline`); tab Board dan SOP disiapkan sebagai placeholder untuk E4/E6.
- **Acceptance criteria:**
  - [ ] Manager bisa menambah/menghapus anggota dari UI; anggota biasa hanya melihat.
