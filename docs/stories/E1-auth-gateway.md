# E1 · Auth & Gateway

Tujuan epic: user bisa login dan semua request melewati Gateway yang memvalidasi token dan membatasi laju request.
Referensi utama: `ARCHITECTURE.md` A.5, B.1, G.1, G.4. Keputusan D2 (identity provider) di `PLANNING.md` harus diputuskan sebelum NX-012; story di bawah mengasumsikan Auth Service sendiri dengan OpenIddict.

---

### NX-011 · API Gateway YARP
- **Sprint:** 2 · **SP:** 3 · **Bergantung pada:** NX-003, NX-010
- **Langkah:**
  1. Buat `src/ApiGateway/NexUS.Gateway.Api` dengan YARP.
  2. Konfigurasi route `/api/v1/auth|users|roles|projects|tasks|sops|notifications/**` ke cluster masing-masing (ARCHITECTURE A.5); alamat dari konfigurasi per environment.
  3. Tambahkan CORS (hanya origin frontend), header keamanan, dan correlation id.
  4. Teruskan `/hubs/notifications` (WebSocket) ke Notification Service.
  5. Tambahkan `Dockerfile` Gateway dengan pola yang sama seperti service lain (ditunda dari NX-007 karena project Gateway baru dibuat di sini).
  6. Pasang `AddNexusSerilog`, `UseNexusObservability`, dan `AddNexusHealthChecks` di Gateway, lalu buktikan `correlationId` yang sama muncul di log Gateway dan service tujuan (ditunda dari NX-010 karena Gateway belum ada).
- **Acceptance criteria:**
  - [ ] `GET /api/v1/projects/health` lewat Gateway diteruskan ke Project Service.
  - [ ] Request dari origin yang tidak diizinkan ditolak CORS.
  - [ ] Route yang tidak terdaftar mengembalikan 404.

---

### NX-012 · Login → access token + refresh token
- **Sprint:** 2 · **SP:** 8 · **Bergantung pada:** NX-009, NX-011
- **Langkah:**
  1. Migrasi `db/auth/V2__auth_tables.sql`: `CREDENTIALS`, `REFRESH_TOKENS`, `CLIENTS`, `LOGIN_HISTORY` (ARCHITECTURE B.1).
  2. Pasang OpenIddict (atau implementasi JWT RS256) dengan kunci privat dari secret; publikasikan `/.well-known/jwks.json`.
  3. Endpoint `POST /api/v1/auth/login`: verifikasi password (hash BCrypt/Argon2), kunci akun setelah 5 gagal dalam 15 menit, catat `LOGIN_HISTORY`.
  4. Terbitkan access token 15 menit dengan klaim `sub`, `tid`, `jti`, `roles`; refresh token 7 hari disimpan sebagai hash di Oracle dan dikirim lewat cookie `HttpOnly; Secure; SameSite=Strict`.
  5. Seed data dev: tenant demo dan user `admin@demo.local`.
- **Acceptance criteria:**
  - [ ] Login benar → 200 + access token + cookie refresh token.
  - [ ] Login salah → 401 dengan pesan generik (tidak membocorkan apakah email terdaftar).
  - [ ] 6 kali gagal berturut-turut → akun terkunci sementara.
  - [ ] Token bisa diverifikasi memakai JWKS.

---

### NX-013 · Refresh token rotasi + deteksi reuse
- **Sprint:** 2 · **SP:** 5 · **Bergantung pada:** NX-012
- **Langkah:**
  1. `POST /api/v1/auth/refresh` membaca cookie, memvalidasi hash, menerbitkan pasangan token baru, dan menandai token lama `revoked_at`.
  2. Jika token yang sudah di-revoke dipakai lagi → revoke seluruh token keluarga sesi tersebut.
  3. Simpan cache `auth:rt:{tokenId}` di Redis.
- **Acceptance criteria:**
  - [ ] Refresh berhasil menghasilkan token baru dan token lama tidak bisa dipakai lagi.
  - [ ] Pemakaian ulang token lama mencabut semua sesi terkait dan mengembalikan 401.

---

### NX-014 · Logout + blacklist jti
- **Sprint:** 2 · **SP:** 3 · **Bergantung pada:** NX-012
- **Langkah:**
  1. `POST /api/v1/auth/logout`: revoke refresh token, hapus cookie, simpan `auth:revoked:{jti}` di Redis dengan TTL sisa umur access token.
- **Acceptance criteria:**
  - [ ] Setelah logout, access token lama ditolak (401) oleh Gateway walau belum kedaluwarsa.

---

### NX-015 · Validasi JWT di Gateway & service
- **Sprint:** 2 · **SP:** 3 · **Bergantung pada:** NX-012, NX-014
- **Langkah:**
  1. Lengkapi `AddNexusJwtAuth()` di BuildingBlocks.Auth (ARCHITECTURE G.1): authority, audience, validasi lifetime, cek blacklist Redis.
  2. Pasang di Gateway (policy `default` untuk route non-auth) dan di setiap service (`[Authorize]` default via fallback policy).
  3. Implementasikan `ICurrentUser` dari klaim (`UserId`, `TenantId`).
  4. Pindahkan `app.UseNexusLogEnrichment()` di setiap `Program.cs` ke setelah `UseAuthentication()`/`UseAuthorization()` (sekarang berada sebelum autentikasi sehingga `TenantId`/`UserId` masih null), dan pastikan `ExceptionHandlingMiddleware` berada di dalam scope enrichment tersebut (temuan review NX-010).
- **Acceptance criteria:**
  - [ ] Request tanpa token ke `/api/v1/projects` → 401 di Gateway.
  - [ ] Service menolak token dengan signature atau audience salah, walau dipanggil langsung tanpa Gateway.

---

### NX-016 · Rate limiting di Gateway
- **Sprint:** 2 · **SP:** 5 · **Bergantung pada:** NX-011, NX-015
- **Langkah:**
  1. Policy `login`, `per-user`, `per-tenant`, `upload` sesuai tabel ARCHITECTURE G.4.
  2. Gunakan penyimpanan Redis agar batas berlaku untuk semua replika Gateway.
  3. Response 429 menyertakan header `Retry-After`.
- **Acceptance criteria:**
  - [ ] Request ke-301 dalam satu menit oleh user yang sama → 429.
  - [ ] Dua instance Gateway berbagi hitungan yang sama.

---

### NX-017 · Halaman login React + interceptor refresh
- **Sprint:** 2 · **SP:** 5 · **Bergantung pada:** NX-004, NX-013
- **Langkah:**
  1. `features/auth`: `LoginPage` (React Hook Form + Zod), `authStore` (Zustand, token di memori — ARCHITECTURE E.3).
  2. Interceptor axios: sisipkan Bearer token, satu kali refresh saat 401, logout saat refresh gagal (ARCHITECTURE E.5).
  3. Saat aplikasi dimuat, panggil `/auth/refresh` untuk memulihkan sesi.
  4. `RequireAuth` di router; tombol logout di header.
- **Acceptance criteria:**
  - [ ] Login berhasil membawa user ke Dashboard; refresh halaman tidak mengeluarkan user.
  - [ ] Token tidak ada di `localStorage` maupun `sessionStorage`.
  - [ ] Akses route terproteksi tanpa login diarahkan ke `/login`.
