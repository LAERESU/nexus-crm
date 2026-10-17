# E0 · Fondasi & DevOps

Tujuan epic: kerangka repo, kode bersama, dan pipeline siap sehingga service bisnis bisa dibangun dengan pola yang sama.
Referensi utama: `ARCHITECTURE.md` bagian D dan F.

---

### NX-001 · Inisialisasi monorepo ✅
- **Sprint:** 0 · **SP:** 2 · **Bergantung pada:** —
- **Hasil:** struktur folder, `README.md`, `.editorconfig`, `.gitignore`, `.gitattributes`, `CLAUDE.md`, dokumen `docs/`, branch `main` dan `develop`.

---

### NX-002 · Solution .NET + BuildingBlocks ✅
- **Sprint:** 1 · **SP:** 5 · **Bergantung pada:** NX-001
- **Tujuan:** satu solution `NexUS.sln` dan pustaka bersama yang dipakai semua service.
- **Langkah:**
  1. Buat `global.json` (SDK .NET 10) dan `Directory.Build.props` di root: `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true` untuk Release, `LangVersion=latest`.
  2. Buat `Directory.Packages.props` (Central Package Management) dan daftarkan versi paket: MediatR, FluentValidation, Serilog.AspNetCore, StackExchange.Redis, MassTransit.RabbitMQ, Oracle.ManagedDataAccess.Core, Oracle.EntityFrameworkCore, Dapper, OpenTelemetry.*, xUnit, FluentAssertions, Testcontainers.
  3. Buat `NexUS.sln` dan class library di `src/BuildingBlocks/`:
     - `NexUS.BuildingBlocks.Common` — `Result<T>`, `PagedResult<T>`, `Error`, exception dasar (`DomainException`, `NotFoundException`, `ForbiddenException`), `AggregateRoot`, `IDomainEvent`, `ICurrentUser`.
     - `NexUS.BuildingBlocks.Auth` — extension `AddNexusJwtAuth()` dan permission policy (isi lengkap di NX-015/NX-020; di sini cukup kerangka).
     - `NexUS.BuildingBlocks.Caching` — `ICacheService` + `RedisCacheService` dengan graceful degradation (ARCHITECTURE D.5).
     - `NexUS.BuildingBlocks.Messaging` — `OutboxMessage`, kontrak `IIntegrationEvent` (implementasi outbox di NX-028).
     - `NexUS.BuildingBlocks.Observability` — extension `AddNexusSerilog()` (diisi di NX-010).
  4. Buat project test `tests/NexUS.BuildingBlocks.Tests` dengan test untuk `Result<T>`, `PagedResult<T>`, dan `RedisCacheService` (mock `IConnectionMultiplexer`).
  5. Hapus `.gitkeep` di folder yang sekarang sudah berisi file.
- **Acceptance criteria:**
  - [x] `dotnet build NexUS.sln -c Release` sukses tanpa warning.
  - [x] `dotnet test` lulus.
  - [x] Versi paket hanya didefinisikan di `Directory.Packages.props`.
  - [x] `RedisCacheService` mengembalikan `default` (bukan exception) ketika Redis tidak tersedia.

---

### NX-003 · Template service Clean Architecture ✅
- **Sprint:** 1 · **SP:** 5 · **Bergantung pada:** NX-002
- **Tujuan:** pola service yang bisa disalin untuk ketujuh service.
- **Langkah:**
  1. Buat service contoh pertama yang nyata: **Project** — `src/Services/Project/` berisi `NexUS.Project.Domain`, `.Application`, `.Infrastructure`, `.Api`, dan `tests/NexUS.Project.UnitTests`, `tests/NexUS.Project.IntegrationTests`.
  2. Atur reference sesuai aturan dependency (CONTEXT bagian 3 poin 5).
  3. Di Application: daftarkan MediatR, FluentValidation, dan pipeline behavior `ValidationBehavior` dan `LoggingBehavior`.
  4. Di Api: `Program.cs` dengan Swagger, ProblemDetails, `ExceptionHandlingMiddleware` (pemetaan exception → status, ARCHITECTURE D.6), endpoint `/health/live` dan `/health/ready`.
  5. Tambahkan test arsitektur (NetArchTest) yang gagal bila Domain mereferensi Application/Infrastructure.
  6. Tulis `docs/adr/0003-template-service.md` yang menjelaskan struktur ini, lalu buat kerangka kosong yang sama (project + `Program.cs` + health) untuk Auth, User, Task, Sop, Notification, Audit.
- **Acceptance criteria:**
  - [x] Ketujuh service ter-build dan bisa dijalankan (`dotnet run`) dengan `/health/live` mengembalikan 200.
  - [x] Swagger tampil di `/swagger` untuk setiap service pada environment Development.
  - [x] Test arsitektur lulus.
  - [x] Exception yang tidak tertangani menghasilkan ProblemDetails tanpa stack trace.

---

### NX-004 · Inisialisasi frontend React ✅
- **Sprint:** 1 · **SP:** 3 · **Bergantung pada:** NX-001
- **Tujuan:** aplikasi React siap dikembangkan per fitur.
- **Langkah:**
  1. `npm create vite@latest web -- --template react-ts` (hapus `.gitkeep` lebih dulu).
  2. Aktifkan TypeScript `strict`, alias `@/` → `src/`.
  3. Pasang: react-router-dom, @tanstack/react-query, zustand, axios, react-hook-form, zod, @hookform/resolvers, antd (atau MUI, catat pilihan di ADR), dan untuk dev: eslint, prettier, vitest, @testing-library/react.
  4. Buat struktur folder sesuai ARCHITECTURE E.1 dengan `MainLayout`, `AuthLayout`, halaman `DashboardPage` kosong, `NotFoundPage`, `ForbiddenPage`.
  5. Buat `shared/api/httpClient.ts` (interceptor lengkap di NX-017) dan `queryClient.ts`.
  6. Tambahkan `.env.example` (`VITE_API_BASE_URL=/api/v1`) dan proxy dev Vite ke Gateway.
  7. Script npm: `dev`, `build`, `lint`, `test`, `format`.
- **Acceptance criteria:**
  - [x] `npm run build`, `npm run lint`, dan `npm test` lulus.
  - [x] `npm run dev` menampilkan layout dengan sidebar dan halaman Dashboard kosong.
  - [x] Tidak ada pemakaian `any` di kode awal.

---

### NX-005 · docker-compose lokal lengkap ✅
- **Sprint:** 1 · **SP:** 5 · **Bergantung pada:** NX-003, NX-007
- **Tujuan:** satu perintah menjalankan semua infrastruktur dan service di laptop.
- **Langkah:**
  1. Lengkapi `deploy/docker-compose.yml` (sudah berisi Oracle, Redis, RabbitMQ) dengan object storage S3 lokal (MinIO diganti RustFS, lihat ADR-0005) dan Mailpit (SMTP palsu untuk uji email).
  2. Tambahkan service aplikasi (gateway, 7 service, web) yang dibangun dari Dockerfile NX-007, dengan `depends_on` kondisi `service_healthy`.
  3. Buat skrip init Oracle `deploy/oracle-init/01-users.sql` yang membuat user/schema `AUTH_SCHEMA`, `USR_SCHEMA`, `PRJ_SCHEMA`, `TSK_SCHEMA`, `SOP_SCHEMA`, `NTF_SCHEMA`, `AUD_SCHEMA` beserta grant minimal.
  4. Buat `docker-compose.override.yml` untuk mode dev (hanya infrastruktur), sehingga service bisa dijalankan dari IDE.
  5. Dokumentasikan di `README.md`.
- **Acceptance criteria:**
  - [x] `docker compose up -d` dari folder `deploy` menjalankan semua container sampai `healthy`. (13 container; Gateway menyusul di NX-011)
  - [x] Ketujuh schema Oracle ada dan bisa diakses dengan user masing-masing.
  - [x] UI RabbitMQ, RustFS, dan Mailpit bisa dibuka di browser. (diverifikasi HTTP 200)
- **Catatan implementasi:**
  - Skrip init berupa `01-users.sh` (bukan `.sql`) agar password schema dibaca dari environment, bukan ditulis di repo.
  - Mode dev memakai `docker-compose.dev.yml` yang dipilih eksplisit dengan `-f`, bukan `docker-compose.override.yml`, karena file override dimuat otomatis dan akan membuat `docker compose up -d` tidak menjalankan semua container.
  - Healthcheck service memakai mode `--healthcheck` pada aplikasi karena image chiseled tidak punya shell/curl.

---

### NX-006 · Pipeline CI
- **Sprint:** 1 · **SP:** 5 · **Bergantung pada:** NX-002, NX-003, NX-004
- **Tujuan:** setiap PR otomatis dicek build, test, lint, dan keamanan.
- **Langkah:**
  1. Buat `.github/workflows/ci-backend.yml`: setup .NET 10, restore, build Release, test dengan coverage, gagal bila coverage < 70%.
  2. Buat `.github/workflows/ci-frontend.yml`: Node 20, `npm ci`, lint, test, build.
  3. Tambahkan path filter agar workflow hanya jalan saat folder terkait berubah.
  4. Tambahkan job keamanan: `dotnet list package --vulnerable`, `npm audit --audit-level=high`, CodeQL.
  5. Pindahkan `docs/pull_request_template.md` ke `.github/` (opsional), aktifkan branch protection di GitHub untuk `main` dan `develop`.
  6. Tambahkan job migrasi database (ditunda dari NX-009): jalankan `scripts/check-migrations-immutable.sh origin/develop`, lalu `flyway validate` dan `flyway migrate` terhadap Oracle Free sebagai service container.
- **Acceptance criteria:**
  - [ ] PR ke `develop` menjalankan workflow dan statusnya tampil di PR.
  - [ ] Build gagal jika ada test gagal, lint gagal, atau dependency HIGH/CRITICAL.
  - [ ] Badge status CI tampil di `README.md`.
- **Catatan implementasi:**
  - Workflow: `ci-backend`, `ci-frontend`, `ci-database`, `ci-images` (build + Trivy semua image, ditambahkan karena dijanjikan di NX-007), `codeql`, serta `dependabot.yml`.
  - Gate lokal sudah diuji: coverage 82,2% (ambang 70%), `check-coverage.sh 95` gagal sebagaimana mestinya, `npm audit` bersih, `actionlint` bersih.
  - Workflow baru bisa diverifikasi penuh setelah dijalankan di GitHub; centang tiga kriteria di atas setelah PR pertama hijau.
  - Hasil review PR: nama job dibuat unik (`backend-build-test`, `frontend-build-test`) agar branch protection tidak salah cocok; `cancel-in-progress` hanya untuk PR (push ke develop/main tidak dibatalkan); skrip kerentanan gagal bila data kerentanan tidak terambil (NU1900) atau keluaran kosong; audit mingguan terjadwal; `NuGetAuditLevel=high` agar build dan gate memakai ambang yang sama; `timeout-minutes` di semua job; cache database Trivy; `npm audit --package-lock-only`; Dependabot mencakup Dockerfile service dan compose.
  - Check yang diwajibkan di branch protection nanti: `backend-build-test`, `vulnerable-packages`, `frontend-build-test`, `npm-audit`, `flyway`, `migrations-immutable`, `analyze (...)`, `build-scan (...)`. Karena memakai path filter, wajibkan hanya check yang selalu muncul atau gunakan job penutup bila PR tertahan.
  - Branch protection (langkah 5) diatur manual di GitHub oleh pemilik repo. Jangan wajibkan check yang memakai path filter (mis. `ci-backend`) pada PR yang tidak menyentuh path-nya, karena check itu tidak pernah muncul dan PR tertahan.

---

### NX-007 · Dockerfile backend & frontend ✅
- **Sprint:** 1 · **SP:** 3 · **Bergantung pada:** NX-003, NX-004
- **Langkah:**
  1. Dockerfile multi-stage untuk setiap service di `src/Services/<X>/NexUS.<X>.Api/Dockerfile` dan Gateway (pola ARCHITECTURE F.3, user non-root, port 8080).
  2. `web/Dockerfile` + `web/nginx.conf` (ARCHITECTURE F.4).
  3. `.dockerignore` di root dan `web/`.
- **Acceptance criteria:**
  - [x] Semua image ter-build lokal; ukuran image service < 250 MB. (7 service 186 MB, web 91 MB; Dockerfile Gateway dikerjakan di NX-011)
  - [x] Container berjalan sebagai non-root. (service backend `app`, web `nginx` uid 101)
  - [x] Scan Trivy tanpa CVE HIGH/CRITICAL yang bisa diperbaiki. (8 image: 0 temuan; scan otomatis di CI NX-006)

---

### NX-008 · Helm chart & deploy otomatis ke Kubernetes dev
- **Sprint:** 2 · **SP:** 8 · **Bergantung pada:** NX-006, NX-007
- **Langkah:**
  1. Buat chart generik `deploy/helm/service` (Deployment, Service, HPA, PDB, ConfigMap, probe, securityContext) sesuai ARCHITECTURE F.5.
  2. Buat file values per service per environment: `deploy/helm/values/<service>.<env>.yaml`.
  3. Tambahkan job build-push image ke registry dan `helm upgrade --install --atomic` ke namespace dev di workflow CI saat merge ke `develop`.
  4. Secret diambil dari Kubernetes Secret / External Secrets, bukan dari values.
- **Acceptance criteria:**
  - [ ] Merge ke `develop` otomatis men-deploy ke cluster dev.
  - [ ] Deploy gagal otomatis rollback (`--atomic`).
  - [ ] `kubectl get pods -n nexus-dev` menunjukkan semua pod Ready.
- **Catatan:** jika cluster belum tersedia, gunakan kind/minikube lokal dan tandai keputusan D3 di `PLANNING.md`.

---

### NX-009 · Flyway + schema Oracle per service ✅
- **Sprint:** 1 · **SP:** 5 · **Bergantung pada:** NX-005
- **Langkah:**
  1. Tambahkan service `flyway` di docker-compose yang menjalankan migrasi untuk setiap folder `db/<schema>/`.
  2. Buat `V1__baseline.sql` per schema berisi tabel `OUTBOX_MESSAGES` dan `PROCESSED_EVENTS` (tabel bisnis dibuat di story masing-masing).
  3. Tambahkan skrip `scripts/db-migrate.ps1` dan `.sh`.
  4. Tambahkan langkah validasi migrasi di CI (`flyway validate` terhadap Oracle Free di Testcontainers/service container).
- **Acceptance criteria:**
  - [x] Migrasi berjalan ulang tanpa error (idempotent melalui riwayat Flyway).
  - [x] Setiap schema punya tabel `flyway_schema_history`.
  - [x] CI gagal bila file migrasi lama diubah (checksum mismatch). (`flyway validate` gagal dengan "checksum mismatch" dan `scripts/check-migrations-immutable.sh` gagal bila file lama diubah; pemasangan di workflow CI dikerjakan di NX-006)
- **Catatan implementasi:**
  - Pemetaan folder ke schema: `db/auth`→`AUTH_SCHEMA`, `usr`→`USR_SCHEMA`, `prj`→`PRJ_SCHEMA`, `tsk`→`TSK_SCHEMA`, `sop`→`SOP_SCHEMA`, `ntf`→`NTF_SCHEMA`, `aud`→`AUD_SCHEMA`.
  - Service `flyway` di compose dijalankan sekali dan wajib selesai sukses (`service_completed_successfully`) sebelum service aplikasi start.
  - `flyway validate` pada database bersih tidak mendeteksi file lama yang diubah; karena itu ada pemeriksaan git terpisah (`check-migrations-immutable.sh`).

---

### NX-010 · Logging terstruktur, correlation id, health check
- **Sprint:** 2 · **SP:** 3 · **Bergantung pada:** NX-003
- **Langkah:**
  1. Lengkapi `NexUS.BuildingBlocks.Observability`: Serilog JSON ke console dengan enrich `service`, `environment`, `tenantId`, `userId`, `correlationId`.
  2. Middleware `CorrelationIdMiddleware`: baca/buat `X-Correlation-Id`, kembalikan di response, teruskan ke `HttpClient` lewat `DelegatingHandler`.
  3. Health check `/health/ready` memeriksa Oracle, Redis, dan RabbitMQ.
  4. Pasang di semua service dan Gateway.
- **Acceptance criteria:**
  - [ ] Satu request lewat Gateway menghasilkan log dengan `correlationId` yang sama di Gateway dan service. (sisi service ✅: id dari header dipakai ulang, dikembalikan di response, dan muncul di log JSON; sisi Gateway dikerjakan di NX-011)
  - [x] `/health/ready` mengembalikan 503 saat Oracle mati. (diuji di compose: `docker stop nexus-oracle` → ready 503, live tetap 200; kegagalan Redis/RabbitMQ berstatus Degraded dan tetap 200, lihat catatan)
  - [x] Tidak ada password/token yang tercatat di log. (`SensitiveDataMasker` + tes; log 7 container diperiksa, tidak ada password)
- **Catatan implementasi:**
  - Properti log: `Service`, `Environment`, `CorrelationId`, `TenantId`, `UserId`. TenantId dan UserId dibaca dari claim `tenant_id` dan `NameIdentifier`; nama claim final ditetapkan di NX-012 (login) dan, bila berbeda, konstanta di `ApplicationBuilderExtensions` disesuaikan.
  - `X-Correlation-Id` yang masuk hanya dipakai bila aman (huruf/angka/`-`/`_`, maks 64 karakter); selain itu diganti id baru agar tidak terjadi log injection.
  - Pemeriksaan dependency hanya didaftarkan bila `ConnectionStrings:Oracle|Redis|RabbitMq` dikonfigurasi, sehingga service tetap bisa dijalankan dari IDE tanpa infrastruktur. `/health/live` tidak memeriksa dependency.
  - Hasil review: Redis (hanya cache, CONTEXT.md bagian 3 poin 7) dan RabbitMQ (Outbox) berstatus **Degraded** saat gagal sehingga pod tidak dikeluarkan dari Service; hanya Oracle yang membuat ready 503. Hasil health check tidak membawa exception asli (bisa memuat kredensial). `RedisHealthCheck` memakai satu multiplexer per proses. Request log `/health/*` diturunkan ke Verbose. Correlation id menerima `.` dan `:` (maks 128) agar format trace seperti W3C `traceparent` tidak diganti.
  - Dari hasil review, ditunda dengan sengaja: urutan `UseNexusLogEnrichment` setelah autentikasi (dikerjakan di NX-015 saat autentikasi dipasang), dan pemisahan health check dependency ke project sendiri agar `Observability` tidak membawa klien Oracle/Redis/RabbitMQ ke semua consumer (catatan teknis; dipertimbangkan bila ukuran image atau CVE jadi masalah).
  - `Oracle.ManagedDataAccess.Core` membawa `System.Security.Cryptography.Pkcs` 8.0.1 yang rentan (HIGH); versinya dipaku ke 10.0.13 lewat Central Package Management.
