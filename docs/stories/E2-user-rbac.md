# E2 · User & RBAC

Tujuan epic: admin tenant bisa mengelola user, departemen, role, dan permission; setiap endpoint dilindungi permission.
Referensi utama: `ARCHITECTURE.md` B.2, C.2 (USR_SCHEMA), G.2.

---

### NX-018 · Schema & CRUD tenant, user, departemen
- **Sprint:** 3 · **SP:** 8 · **Bergantung pada:** NX-015
- **Langkah:**
  1. Migrasi `db/usr/V2__users.sql`: `TENANTS`, `USERS`, `DEPARTMENTS` dengan kolom standar dan index `UX_USERS_TNT_EMAIL`.
  2. Domain: aggregate `User` (status aktif/nonaktif), `Department` (hierarki `PARENT_ID`).
  3. Endpoint sesuai ARCHITECTURE B.2: list (paging, search, filter departemen), detail, `me`, create, update, `PATCH status`.
  4. Event `UserCreated`, `UserUpdated`, `UserDeactivated` via outbox (pakai implementasi NX-028 bila sudah ada; jika belum, simpan ke outbox dan kirim nanti).
- **Acceptance criteria:**
  - [x] Email unik per tenant (case-insensitive).
  - [x] User tenant A tidak bisa melihat user tenant B.
  - [x] Paging maksimum 100 per halaman.
  - [x] Integration test dengan Oracle Testcontainers lulus.

---

### NX-019 · Role & permission + assign role
- **Sprint:** 3 · **SP:** 8 · **Bergantung pada:** NX-018
- **Langkah:**
  1. Migrasi `V3__rbac.sql`: `ROLES`, `PERMISSIONS`, `ROLE_PERMISSIONS`, `USER_ROLES` (dengan `SCOPE_TYPE` dan `SCOPE_ID`).
  2. Seed permission (`project.read`, `task.create`, `sop.approve`, dst.) dan role default (ARCHITECTURE G.2) untuk setiap tenant baru.
  3. Endpoint: CRUD role, atur permission role, `PUT /users/{id}/roles`, `GET /users/{id}/permissions` (internal).
  4. Event `RoleChanged`.
- **Acceptance criteria:**
  - [x] Role sistem (`IS_SYSTEM=1`) tidak bisa dihapus.
  - [x] Perubahan role langsung tercermin pada endpoint permissions.

---

### NX-020 · Permission policy dinamis + cache
- **Sprint:** 3 · **SP:** 5 · **Bergantung pada:** NX-019
- **Langkah:**
  1. Implementasikan `PermissionPolicyProvider` dan `PermissionHandler` di BuildingBlocks.Auth (ARCHITECTURE G.2).
  2. `IPermissionService`: ambil dari Redis `tnt:{t}:perm:{userId}` (TTL 15 menit), fallback ke User Service.
  3. Consumer `RoleChanged` menghapus cache terkait.
  4. Terapkan `[Authorize(Policy = "...")]` di semua endpoint yang sudah ada.
- **Acceptance criteria:**
  - [x] User tanpa `user.manage` mendapat 403 di endpoint admin.
  - [x] Setelah role diubah, akses berubah paling lambat setelah cache terhapus (event) tanpa menunggu TTL.
- **Catatan:** invalidasi dilakukan langsung oleh User Service setelah commit, bukan lewat consumer `RoleChanged` (belum ada RabbitMQ; menyusul setelah NX-028). Retry/circuit breaker Polly untuk panggilan ke User Service ditunda. Lihat ADR-0009.

---

### NX-021 · Undang user via email
- **Sprint:** 3 · **SP:** 3 · **Bergantung pada:** NX-018, NX-012
- **Langkah:**
  1. Auth Service mengonsumsi `UserCreated`, membuat kredensial dengan token aktivasi berumur 72 jam.
  2. Endpoint `POST /api/v1/auth/activate` untuk set password pertama.
  3. Email undangan dikirim (sementara langsung via SMTP Mailpit; dipindah ke Notification di NX-057).
- **Acceptance criteria:**
  - [x] User baru menerima email dan bisa login setelah set password.
  - [x] Token aktivasi kedaluwarsa tidak bisa dipakai.
- **Catatan:** relay outbox ke RabbitMQ dan consumer idempotent (bagian NX-028) dikerjakan di sini, memakai `RabbitMQ.Client` langsung karena MassTransit 9 berlisensi komersial. Halaman web `/activate` ditambahkan menyusul sebagai perbaikan (token dibaca dari fragment `#token=` agar tidak tercatat di log server); kirim ulang undangan belum ada. Lihat ADR-0010.

---

### NX-022 · Halaman user list + form user
- **Sprint:** 3 · **SP:** 5 · **Bergantung pada:** NX-017, NX-018
- **Langkah:**
  1. `features/users`: `UserListPage` (tabel, search, filter status/departemen, paging server-side).
  2. `InviteUserModal` dan drawer edit user; aksi aktif/nonaktif.
- **Acceptance criteria:**
  - [x] Search ber-debounce dan paging berjalan dari server.
  - [x] Error validasi dari server tampil di field yang tepat.
- **Catatan:** halaman di `/admin/users`; menu Admin belum disaring menurut permission (NX-024). Pilihan departemen memuat maksimal 100 departemen.

---

### NX-023 · Halaman role + matriks permission
- **Sprint:** 3 · **SP:** 5 · **Bergantung pada:** NX-019, NX-022
- **Langkah:**
  1. `RoleManagementPage` dengan `PermissionMatrix` (baris permission dikelompokkan per modul, kolom role, checkbox).
  2. Simpan perubahan per role; role sistem ditampilkan read-only.
- **Acceptance criteria:**
  - [x] Perubahan checkbox tersimpan dan bertahan setelah reload.
- **Catatan:** halaman di `/admin/roles` (butuh `role.read`; mengubah butuh `role.manage` global). Matriks memuat maksimal 100 role. Membuat, mengganti nama, dan menghapus role belum ada di UI (tidak termasuk story ini).

---

### NX-024 · Proteksi route & komponen Can
- **Sprint:** 3 · **SP:** 3 · **Bergantung pada:** NX-020, NX-017
- **Langkah:**
  1. Simpan daftar permission di `authStore` saat login/refresh.
  2. `RequireAuth permission="..."` di router (ARCHITECTURE E.2) dan komponen `<Can permission>`.
  3. Sidebar hanya menampilkan menu yang diizinkan.
- **Acceptance criteria:**
  - [x] User tanpa izin yang membuka URL admin diarahkan ke `/403`.
  - [x] Tombol aksi tersembunyi untuk user tanpa permission terkait.
- **Catatan:** permission dimuat dari `GET /users/me/permissions` (endpoint baru; `{id}/permissions` diblokir Gateway) setiap access token berganti (respons login tidak memuatnya). `/admin/users` butuh `user.read`; tombol aksinya butuh `user.manage` di scope global, sama dengan policy backend.
