# E6 · SOP Builder

Tujuan epic: perusahaan bisa menulis, me-review, menyetujui, menerbitkan, dan memversikan SOP sendiri, lalu memakainya sebagai checklist tugas.
Referensi utama: `ARCHITECTURE.md` B.5, C.2 (SOP_SCHEMA), C.4, bagian H seluruhnya.

---

### NX-044 · Schema SOP
- **Sprint:** 6 · **SP:** 5 · **Bergantung pada:** NX-028
- **Langkah:**
  1. Migrasi `db/sop/V2__sop.sql`: `SOP_CATEGORIES`, `SOP_DOCUMENTS`, `SOP_VERSIONS` (DDL ARCHITECTURE C.4 termasuk `UX_SOP_ONE_PUBLISHED`), `SOP_STEPS`, `SOP_TEMPLATES`.
  2. Domain: aggregate `SopDocument` yang memiliki `SopVersion`; status versi `DRAFT, IN_REVIEW, APPROVED, REJECTED, PUBLISHED, SUPERSEDED, ARCHIVED`.
- **Acceptance criteria:**
  - [ ] Database menolak dua versi `PUBLISHED` untuk SOP yang sama.
  - [ ] Unit test transisi status versi lulus.

---

### NX-045 · CRUD kategori & dokumen SOP
- **Sprint:** 6 · **SP:** 5 · **Bergantung pada:** NX-044
- **Langkah:** endpoint kategori (hierarki), `POST /sops` membuat dokumen + versi draft 0.1 dari template, kode otomatis `SOP-{KAT}-{NNN}` per tenant (sequence aman terhadap konkurensi), list & search; halaman `SopListPage`.
- **Acceptance criteria:**
  - [ ] Dua SOP yang dibuat bersamaan tidak mendapat kode yang sama.
  - [ ] SOP bisa difilter per kategori dan status.

---

### NX-046 · Editor TipTap + autosave
- **Sprint:** 6 · **SP:** 8 · **Bergantung pada:** NX-045
- **Langkah:**
  1. `SopEditorPage` dan komponen `SopEditor` (ARCHITECTURE H.3): heading, list, tabel, gambar (upload ke MinIO), link, callout, node kustom `SopStepBlock`.
  2. Panel kanan untuk metadata (pemilik, tanggal efektif, tanggal review) dan `StepListEditor`.
  3. Autosave setiap 10 detik ke `PUT /sops/{id}/versions/{versionId}` dengan indikator status simpan.
  4. Langkah dari `SopStepBlock` disinkronkan ke tabel `SOP_STEPS` saat simpan.
- **Acceptance criteria:**
  - [ ] Menutup tab lalu membuka lagi menampilkan isi terakhir yang tersimpan.
  - [ ] `CONTENT_JSON` bisa dimuat ulang ke editor tanpa kehilangan format.

---

### NX-047 · Sanitasi HTML di server
- **Sprint:** 6 · **SP:** 3 · **Bergantung pada:** NX-046
- **Langkah:** pasang HtmlSanitizer dengan allowlist tag/atribut sesuai ekstensi editor; sanitasi `CONTENT_HTML` sebelum disimpan; test dengan payload XSS umum.
- **Acceptance criteria:**
  - [ ] `<script>`, `onerror=`, dan `javascript:` hilang dari HTML tersimpan.

---

### NX-048 · Lock edit SOP
- **Sprint:** 6 · **SP:** 3 · **Bergantung pada:** NX-046
- **Langkah:** `POST/DELETE /sops/{id}/lock` dengan Redis `SET NX EX 300`, perpanjangan tiap 2 menit dari frontend; editor read-only bila lock dipegang orang lain.
- **Acceptance criteria:**
  - [ ] User kedua melihat pesan "Sedang diedit oleh …" dan tidak bisa menyimpan.
  - [ ] Lock lepas otomatis 5 menit setelah tab ditutup.

---

### NX-049 · Versioning
- **Sprint:** 7 · **SP:** 8 · **Bergantung pada:** NX-046
- **Langkah:** `POST /sops/{id}/versions` (pilihan major/minor, salin dari versi published), aturan satu draft aktif, versi `PUBLISHED` immutable, `CHANGE_SUMMARY` wajib sebelum submit, halaman `VersionHistory` (ARCHITECTURE H.4).
- **Acceptance criteria:**
  - [ ] Edit pada versi published → 422.
  - [ ] Membuat draft kedua saat sudah ada draft → 422.

---

### NX-050 · Diff antar versi
- **Sprint:** 7 · **SP:** 5 · **Bergantung pada:** NX-049
- **Langkah:** `DiffViewer` membandingkan dua versi (teks dan daftar langkah) dengan highlight tambah/hapus; endpoint `GET /versions/{a}/diff/{b}` mengembalikan kedua konten.
- **Acceptance criteria:**
  - [ ] Perubahan satu kalimat dan satu langkah terlihat jelas pada diff.

---

### NX-051 · Approval berjenjang
- **Sprint:** 7 · **SP:** 8 · **Bergantung pada:** NX-049, NX-020
- **Langkah:**
  1. Tabel `SOP_APPROVAL_FLOWS` (konfigurasi level per kategori oleh admin) dan `SOP_APPROVALS`.
  2. Endpoint submit, approve, reject (ARCHITECTURE H.5, contoh handler) dengan segregation of duties.
  3. Halaman `SopApprovalPage` (antrian) dan `ApprovalPanel` di halaman SOP.
  4. Event `SopSubmitted`, `SopApproved`, `SopRejected`.
- **Acceptance criteria:**
  - [ ] Penulis tidak bisa menyetujui SOP-nya sendiri (403).
  - [ ] Level 2 tidak bisa menyetujui sebelum level 1 setuju.
  - [ ] Reject mengembalikan versi ke `DRAFT` beserta catatannya.

---

### NX-052 · Publish SOP
- **Sprint:** 7 · **SP:** 3 · **Bergantung pada:** NX-051
- **Langkah:** `POST .../publish` dalam satu transaksi (ARCHITECTURE H.2: versi lama `SUPERSEDED`, pointer `CURRENT_VERSION_ID` pindah); cache `sop:{id}:published`; halaman baca `SopViewPage` dengan daftar isi; event `SopPublished`.
- **Acceptance criteria:**
  - [ ] Hanya versi `APPROVED` yang bisa diterbitkan.
  - [ ] Halaman baca selalu menampilkan versi yang berlaku.

---

### NX-053 · Acknowledgement
- **Sprint:** 8 · **SP:** 5 · **Bergantung pada:** NX-052
- **Langkah:** tabel `SOP_ACKNOWLEDGEMENTS`; tombol "Saya sudah membaca dan memahami"; target pembaca per SOP (role/departemen); laporan siapa yang belum membaca; versi major baru mewajibkan baca ulang.
- **Acceptance criteria:**
  - [ ] Laporan menampilkan persentase dan daftar user yang belum membaca.
  - [ ] Versi minor tidak mereset acknowledgement.

---

### NX-054 · SOP ke proyek + checklist tugas
- **Sprint:** 8 · **SP:** 8 · **Bergantung pada:** NX-052, NX-037
- **Langkah:**
  1. Project Service: `PROJECT_SOP_LINKS`, endpoint `POST /projects/{id}/sops`, tab SOP di detail proyek.
  2. Task Service: `POST /tasks/{id}/apply-sop/{sopVersionId}` membuat item checklist dari `SOP_STEPS` dengan `SOP_STEP_ID`.
  3. Consumer `SopPublished` menandai checklist yang memakai versi lama sebagai "SOP diperbarui" (tidak mengubah otomatis).
- **Acceptance criteria:**
  - [ ] Menerapkan SOP 5 langkah menghasilkan 5 item checklist berurutan.
  - [ ] Tugas menampilkan tautan ke versi SOP yang dipakai.
