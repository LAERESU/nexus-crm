# E1 · Auth & Gateway

Tujuan epic: user bisa login dan semua request melewati Gateway yang memvalidasi token dan membatasi laju request.
Referensi utama: `ARCHITECTURE.md` A.5, B.1, G.1, G.4. Keputusan D2 (identity provider) di `PLANNING.md` harus diputuskan sebelum NX-012; story di bawah mengasumsikan Auth Service sendiri dengan OpenIddict.

---

### NX-011 · API Gateway YARP ✅
- **Sprint:** 2 · **SP:** 3 · **Bergantung pada:** NX-003, NX-010
- **Langkah:**
  1. Buat `src/ApiGateway/NexUS.Gateway.Api` dengan YARP.
  2. Konfigurasi route `/api/v1/auth|users|roles|projects|tasks|sops|notifications/**` ke cluster masing-masing (ARCHITECTURE A.5); alamat dari konfigurasi per environment.
  3. Tambahkan CORS (hanya origin frontend), header keamanan, dan correlation id.
  4. Teruskan `/hubs/notifications` (WebSocket) ke Notification Service.
  5. Tambahkan `Dockerfile` Gateway dengan pola yang sama seperti service lain (ditunda dari NX-007 karena project Gateway baru dibuat di sini).
  6. Pasang `AddNexusSerilog`, `UseNexusObservability`, dan `AddNexusHealthChecks` di Gateway, lalu buktikan `correlationId` yang sama muncul di log Gateway dan service tujuan (ditunda dari NX-010 karena Gateway belum ada).
- **Acceptance criteria:**
  - [x] `GET /api/v1/projects/health` lewat Gateway diteruskan ke Project Service. (route khusus menulis ulang ke `/health/live` milik service; tes integrasi)
  - [x] Request dari origin yang tidak diizinkan ditolak CORS. (403 di Gateway, request tidak sampai ke service; tes integrasi)
  - [x] Route yang tidak terdaftar mengembalikan 404. (404 ProblemDetails; tes integrasi)
- **Catatan implementasi:**
  - Route: `auth`→Auth, `users` dan `roles`→User, `projects`→Project, `tasks`→Task, `sops`→Sop, `notifications` dan `/hubs/notifications`→Notification. Audit belum diekspos karena belum ada story API-nya.
  - Alamat cluster default = nama service di docker compose (`http://<service>:8080`); `appsettings.Development.json` memakai `localhost:5101…5106`; Kubernetes menimpanya lewat `ReverseProxy__Clusters__<cluster>__Destinations__d1__Address`. Gateway lokal di port 5100 (sesuai proxy Vite).
  - CORS bawaan ASP.NET hanya tidak menambahkan header untuk origin asing sehingga request tetap sampai ke service; karena itu ada `RejectDisallowedOriginMiddleware` yang menolak 403 di Gateway memakai policy CORS yang sama. Request tanpa `Origin` (antar service, curl) tetap diteruskan. **Origin publik aplikasi sendiri wajib didaftarkan** di `Cors:AllowedOrigins` per environment (tidak ada pengecualian same-origin karena Host/scheme di belakang ingress tidak dapat dipercaya).
  - Hasil review: health publik diarahkan ke `/health/live` (murah, tidak membuka status Oracle); endpoint internal `/api/v1/users/{id}/permissions` diblokir (404) di Gateway lewat `GatewayRoutes.InternalPaths`; CORS mengizinkan header klien SignalR; hub memakai cluster `notification-hub` dengan timeout 2 menit untuk long polling; `UseExceptionHandler` agar error Gateway berupa ProblemDetails; Gateway tidak lagi menerima password Redis.
  - Correlation id yang dibuat Gateway ditambahkan ke request yang diteruskan (transform YARP), sehingga service memakai id yang sama.
  - Belum: rate limiting (NX-016) dan validasi JWT (NX-015). Proxy `/api` di `web/nginx.conf` ke Gateway dikerjakan saat halaman login dibuat (NX-017).

---

### NX-012 · Login → access token + refresh token ✅
- **Sprint:** 2 · **SP:** 8 · **Bergantung pada:** NX-009, NX-011
- **Langkah:**
  1. Migrasi `db/auth/V2__auth_tables.sql`: `CREDENTIALS`, `REFRESH_TOKENS`, `CLIENTS`, `LOGIN_HISTORY` (ARCHITECTURE B.1).
  2. Pasang OpenIddict (atau implementasi JWT RS256) dengan kunci privat dari secret; publikasikan `/.well-known/jwks.json`.
  3. Endpoint `POST /api/v1/auth/login`: verifikasi password (hash BCrypt/Argon2), kunci akun setelah 5 gagal dalam 15 menit, catat `LOGIN_HISTORY`.
  4. Terbitkan access token 15 menit dengan klaim `sub`, `tid`, `jti`, `roles`; refresh token 7 hari disimpan sebagai hash di Oracle dan dikirim lewat cookie `HttpOnly; Secure; SameSite=Strict`.
  5. Seed data dev: tenant demo dan user `admin@demo.local`.
- **Acceptance criteria:**
  - [x] Login benar → 200 + access token + cookie refresh token. (tes integrasi dengan Oracle sungguhan di CI)
  - [x] Login salah → 401 dengan pesan generik (tidak membocorkan apakah email terdaftar). (pesan dan waktu respons sama untuk email tak dikenal, password salah, dan akun terkunci)
  - [x] 6 kali gagal berturut-turut → akun terkunci sementara. (tes integrasi: percobaan ke-6 dengan password benar tetap 401, `LOCKED_UNTIL` terisi)
  - [x] Token bisa diverifikasi memakai JWKS. (tes unit dan integrasi memvalidasi token hanya dengan `/.well-known/jwks.json`)
- **Catatan implementasi:**
  - Keputusan D2 dan turunannya di ADR-0006: Auth Service sendiri, token dengan `Microsoft.IdentityModel.JsonWebTokens` (RS256, bukan OpenIddict), email unik global (pencarian kredensial saat login adalah satu-satunya query tanpa filter `TENANT_ID`), klaim `roles` lewat port `IUserDirectory` dengan data sementara di `CREDENTIALS.ROLES`.
  - Lockout: percobaan gagal ke-5 dalam 15 menit mengunci akun 15 menit; percobaan ke-6 (walau password benar) tetap 401 dengan pesan generik yang sama, sehingga status akun tidak bocor. Email tidak dikenal tetap menjalankan verifikasi BCrypt palsu agar waktu respons tidak membocorkan keberadaan email.
  - Refresh token: 256 bit acak, hanya SHA-256-nya yang disimpan; cookie `nexus_rt` dengan `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`.
  - `LOGIN_HISTORY` tidak menyimpan email yang dicoba (data pribadi). Event `UserLoggedIn` dan `CredentialLocked` masuk `OUTBOX_MESSAGES` dalam transaksi yang sama.
  - Seed dev: `admin@demo.local` (tenant 1, role `admin`) dibuat saat start di Development bila `Seed:AdminPassword` diisi (`SEED_ADMIN_PASSWORD` di `deploy/.env`). Kunci RS256 wajib dari secret (`Auth:Jwt:SigningKeyPem`) di luar Development.
  - Tes: 30 unit test (aturan lockout, handler, retry konkurensi, token terverifikasi lewat JWKS) dan tes integrasi dengan Oracle sungguhan lewat Testcontainers (trait `Oracle`, berjalan di CI). Tes Oracle menemukan bug yang tidak terlihat di unit test: `HasConversion<int>()` pada kolom bool membuat filter soft delete gagal di provider Oracle.
  - Hasil review: percobaan gagal paralel kini tetap terhitung (konflik `ROW_VERSION` → muat ulang → hitung lagi, maks 3 kali; konflik menjadi 409, bukan 500); BCrypt selalu dijalankan sebelum cek kunci agar akun terkunci tidak merespons lebih cepat; BCrypt "enhanced" (pre-hash SHA-384) agar password > 72 byte tidak terpotong; IP klien asli dari `X-Forwarded-For` Gateway; seeder berjalan di latar belakang dengan retry dan memfilter tenant; endpoint `openid-configuration` dihapus (jwks_uri dari header Host bisa dipalsukan); nama alasan gagal login dibuat netral (`UserNotFound`, `InvalidLogin`, `Locked`) atas temuan CodeQL.

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
- **Catatan implementasi:**
  - Migrasi baru `V3__refresh_token_family.sql` (V2 sudah di-merge): `FAMILY_ID` mengelompokkan token hasil rotasi dari satu login, `REVOKED_REASON` (`Rotated`, `ReuseDetected`, `Logout`).
  - Nilai refresh token berformat `{tenantId}.{acak}` sehingga pencarian token berdasarkan hash tetap memfilter `TENANT_ID`; pengecualian ADR-0006 tetap hanya satu (pencarian email saat login).
  - Pemakaian ulang token yang sudah dicabut → seluruh token aktif keluarga itu dicabut dengan satu `UPDATE` atomik (`ROW_VERSION` ikut naik, jadi rotasi yang berjalan bersamaan gagal). Sesi lain user yang sama (keluarga berbeda) tidak terpengaruh.
  - Refresh bersamaan (mis. beberapa tab dibuka sekaligus): token yang baru dirotasi dan dipakai lagi dalam **30 detik** (`ReuseGracePeriod`) ditolak 401 **tanpa** mencabut keluarga dan **tanpa** menghapus cookie, karena cookie klien kemungkinan sudah diganti token baru oleh tab pemenang. Lewat dari 30 detik dianggap pencurian. Frontend (NX-017) sebaiknya tetap melakukan refresh satu per satu dalam satu tab.
  - Umur sesi absolut 7 hari sejak login: rotasi tidak memperpanjang `EXPIRES_AT`, sehingga token curian tidak bisa dipakai memperpanjang sesi selamanya (CONTEXT.md bagian 8).
  - Kredensial terkunci **tidak** menolak refresh: lockout bisa dipicu siapa pun yang tahu email korban dan tidak boleh mengeluarkan sesi aktifnya.
  - Pemakaian ulang dicatat sebagai event `RefreshTokenReuseDetected` di outbox (untuk audit), bukan hanya di log. Access token dan role disiapkan sebelum rotasi disimpan agar rotasi tidak ter-commit tanpa token baru sampai ke klien.
  - Format token berubah dari NX-012 (tanpa awalan tenant); belum ada deployment produksi, jadi tidak perlu masa transisi.
  - **Penyimpangan:** cache Redis `auth:rt:{tokenId}` (langkah 3) sengaja tidak dibuat. Setiap refresh wajib menulis ke Oracle (rotasi), sehingga cache tidak menghemat apa pun, sedangkan status pencabutan yang dibaca dari cache berisiko basi dan meloloskan token yang sudah dicabut (Redis bukan sumber kebenaran, CONTEXT.md bagian 3 poin 7). Redis dipakai untuk blacklist `jti` di NX-014.

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
