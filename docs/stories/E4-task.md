# E4 · Task Management

Tujuan epic: anggota proyek mengelola tugas lewat kanban, dengan assignee, komentar, lampiran, dan checklist.
Referensi utama: `ARCHITECTURE.md` A.2 (contoh alur assign), B.4, C.2 (TSK_SCHEMA), C.4, C.5, D.3–D.6, E.4–E.5.

---

### NX-032 · Schema & CRUD tugas + subtask
- **Sprint:** 5 · **SP:** 8 · **Bergantung pada:** NX-028
- **Langkah:**
  1. Migrasi `db/tsk/V2__tasks.sql`: `TASKS` (DDL ARCHITECTURE C.4), `TASK_ASSIGNEES`, index `IX_TASKS_TNT_PRJ_STATUS`, `IX_TASK_ASSIGNEES_USER`.
  2. Read model `PROJECT_REF` dan `PROJECT_MEMBER_REF` diisi dari event proyek.
  3. Domain `TaskItem` (ARCHITECTURE D.3) dengan subtask via `PARENT_TASK_ID` (maksimum 1 tingkat).
  4. Endpoint create, get, update (If-Match), delete (soft), list dengan filter `projectId`, `assigneeId`, `status`, `due_before`, paging.
  5. Tugas pada proyek yang diarsipkan menjadi read-only (consumer `ProjectArchived`).
- **Acceptance criteria:**
  - [ ] Hanya anggota proyek yang bisa membuat/melihat tugas proyek itu.
  - [ ] Subtask dari subtask ditolak.
  - [ ] Query list memakai index (cek `EXPLAIN PLAN`).

---

### NX-033 · State machine status + riwayat
- **Sprint:** 5 · **SP:** 5 · **Bergantung pada:** NX-032
- **Langkah:**
  1. Value object `TaskStatus` dengan transisi yang diizinkan: `TODO→IN_PROGRESS→IN_REVIEW→DONE`, semua status aktif ↔ `BLOCKED`, aktif → `CANCELLED`, `DONE→IN_PROGRESS` (reopen).
  2. `PATCH /tasks/{id}/status`, tabel `TASK_STATUS_HISTORY`, isi `COMPLETED_AT`.
  3. Event `TaskStatusChanged`.
- **Acceptance criteria:**
  - [ ] Transisi tidak valid → 422 dengan pesan jelas.
  - [ ] Setiap perubahan status tercatat di riwayat.
  - [ ] Unit test mencakup semua transisi.

---

### NX-034 · Assignee + validasi keanggotaan
- **Sprint:** 5 · **SP:** 5 · **Bergantung pada:** NX-032
- **Langkah:** `PUT /tasks/{id}/assignees` (maks 20), validasi lewat read model dengan fallback HTTP ke Project Service (Polly: timeout 2 detik, retry 2, circuit breaker); event `TaskAssigned`.
- **Acceptance criteria:**
  - [ ] Menugaskan bukan anggota proyek → 422.
  - [ ] Jika Project Service mati, validasi tetap jalan dari read model.

---

### NX-035 · Komentar tugas
- **Sprint:** 5 · **SP:** 3 · **Bergantung pada:** NX-032
- **Langkah:** tabel `TASK_COMMENTS`, endpoint list/create, edit/hapus hanya oleh penulis dalam 15 menit; sanitasi teks; event `TaskCommented`.
- **Acceptance criteria:**
  - [ ] Komentar berisi skrip HTML tersimpan sebagai teks aman.

---

### NX-036 · Lampiran tugas
- **Sprint:** 5 · **SP:** 5 · **Bergantung pada:** NX-032, NX-005 (MinIO)
- **Langkah:** upload multipart ke MinIO dengan nama acak, metadata di `TASK_ATTACHMENTS`; batas 20 MB; validasi magic bytes untuk tipe yang diizinkan (pdf, docx, xlsx, png, jpg); unduh lewat presigned URL 5 menit; rate limit policy `upload`.
- **Acceptance criteria:**
  - [ ] File > 20 MB atau tipe palsu (ekstensi .pdf berisi .exe) ditolak.
  - [ ] URL unduh kedaluwarsa setelah 5 menit.

---

### NX-037 · Checklist tugas
- **Sprint:** 5 · **SP:** 3 · **Bergantung pada:** NX-032
- **Langkah:** tabel `TASK_CHECKLIST_ITEMS` (kolom `SOP_STEP_ID` nullable disiapkan untuk NX-054); endpoint tambah, centang, urutkan ulang, hapus.
- **Acceptance criteria:**
  - [ ] Urutan item bertahan setelah reload.
  - [ ] Item yang dicentang mencatat `DONE_BY` dan `DONE_AT`.

---

### NX-038 · Kanban board
- **Sprint:** 5 · **SP:** 8 · **Bergantung pada:** NX-033, NX-031
- **Langkah:**
  1. Endpoint board `GET /tasks?projectId=&size=500` dengan cache `tnt:{t}:board:{projectId}` 30 detik.
  2. `TaskBoardPage` dengan dnd-kit, kolom per status, kartu `TaskCard` (judul, prioritas, due date, avatar assignee).
  3. Drag antar kolom memanggil `PATCH status` dengan optimistic update dan rollback saat gagal (ARCHITECTURE E.5).
  4. Filter assignee/prioritas; tampilan list sebagai alternatif.
- **Acceptance criteria:**
  - [ ] Drag ke kolom yang transisinya tidak valid dibatalkan dengan pesan.
  - [ ] Board 300 tugas tetap responsif (< 1 detik render awal).

---

### NX-039 · Drawer detail tugas
- **Sprint:** 5 · **SP:** 5 · **Bergantung pada:** NX-034..NX-038
- **Langkah:** `TaskDrawer` berisi edit judul/deskripsi/prioritas/due date, assignee, checklist, komentar, lampiran, dan riwayat status; URL `?task=<id>` agar bisa dibagikan.
- **Acceptance criteria:**
  - [ ] Membuka URL dengan `?task=` langsung menampilkan drawer tugas tersebut.

---

### NX-040 · Halaman "Tugas saya"
- **Sprint:** 5 · **SP:** 3 · **Bergantung pada:** NX-034
- **Langkah:** endpoint `GET /tasks/my?status=` (cache 1 menit), halaman dengan grup Terlambat / Hari ini / Minggu ini / Nanti.
- **Acceptance criteria:**
  - [ ] Hanya tugas yang ditugaskan ke user login yang tampil, lintas proyek.
