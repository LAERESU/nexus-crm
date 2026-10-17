# NexUS — Rencana Arsitektur Sistem

> Sumber kebenaran desain teknis. Aturan ringkas yang wajib diikuti ada di `CONTEXT.md`; langkah implementasi per story ada di `stories/`.
> Terakhir diperbarui: 2026-10-17

## Ringkasan Eksekutif

Platform ini dibangun sebagai enam microservice ASP.NET Core di belakang satu API Gateway, dengan Oracle sebagai sumber data utama (satu schema per service), Redis sebagai cache dan penyimpan sesi/rate limit, serta React sebagai frontend SPA. Fitur pembeda adalah **SOP Builder**: setiap perusahaan (tenant) dapat menulis, me-review, menyetujui, dan memversikan SOP sendiri, lalu menautkannya ke proyek dan tugas.

### Tujuan

- Mengelola proyek dan tugas lintas tim dalam satu platform multi-tenant.
- Menyediakan SOP yang terkontrol (draft → review → approved → published) dengan jejak audit lengkap.
- Skalabel per service, dapat di-deploy terpisah, dan aman untuk lingkungan enterprise.

### Asumsi Desain

| Aspek | Asumsi |
| --- | --- |
| Pengguna | Multi-tenant (banyak perusahaan), 1 tenant = 1 organisasi |
| Skala awal | ±5.000 pengguna aktif, ±1 juta task, pertumbuhan audit log tinggi |
| Database | Oracle 19c/23ai, satu instance (atau RAC) dengan schema terpisah per service |
| Runtime | .NET 10 LTS, React 18 + TypeScript + Vite |
| Infrastruktur | Kubernetes (on-prem/OpenShift atau AKS/EKS), Redis 7 |
| Identitas | OAuth2/OIDC + JWT; SSO korporat (Azure AD/Keycloak) sebagai opsi |

### Ringkasan Teknologi

| Lapisan | Teknologi | Peran |
| --- | --- | --- |
| Frontend | React 18, TypeScript, React Router, Zustand, TanStack Query, TipTap | SPA, state UI, data fetching, editor SOP |
| Gateway | YARP (ASP.NET Core) atau Kong/NGINX Ingress | Routing, validasi JWT, rate limiting, CORS |
| Backend | ASP.NET Core 10 Web API, MediatR, FluentValidation, Dapper/EF Core (Oracle provider) | Logika bisnis per bounded context |
| Messaging | RabbitMQ atau Kafka (MassTransit) | Event antar service (asinkron) |
| Database | Oracle Database | Data transaksional per service |
| Cache | Redis 7 (StackExchange.Redis) | Cache, sesi, token blacklist, rate limit, pub/sub ringan |
| Observability | Serilog, OpenTelemetry, ELK atau Grafana (Loki, Tempo, Prometheus) | Log, trace, metrik |
| Deployment | Docker, Kubernetes, Helm, GitHub Actions/GitLab CI/Azure DevOps | Build, release, scaling |

## A. High-Level Architecture

Semua request dari browser masuk lewat satu pintu (API Gateway), diteruskan ke service yang tepat, dan setiap service hanya menyentuh schema Oracle miliknya sendiri. Komunikasi yang butuh jawaban langsung memakai REST/gRPC sinkron; komunikasi yang bersifat "memberi tahu" memakai event asinkron lewat message broker.

### A.1 Diagram Arsitektur (Teks)

```
                         ┌──────────────────────────────┐
                         │   Browser (React SPA + CDN)   │
                         └──────────────┬───────────────┘
                                        │ HTTPS (JWT Bearer)
                         ┌──────────────▼───────────────┐
                         │  Ingress / Load Balancer (TLS)│
                         └──────────────┬───────────────┘
                         ┌──────────────▼───────────────┐
                         │  API Gateway (YARP)           │
                         │  - validasi JWT  - rate limit │──── Redis (rate limit,
                         │  - routing       - CORS       │      token blacklist)
                         └──┬────┬────┬────┬────┬────┬──┘
          ┌─────────────────┘    │    │    │    │    └──────────────────┐
   ┌──────▼─────┐ ┌──────▼─────┐ ┌▼────────┐ ┌▼────────┐ ┌──────▼─────┐ ┌▼──────────────┐
   │Auth Service│ │User Service│ │Project  │ │Task     │ │SOP Service │ │Notification   │
   │OAuth2/JWT  │ │profil, role│ │Service  │ │Service  │ │builder,ver.│ │email/in-app/WS│
   └──┬─────┬───┘ └──┬─────┬───┘ └┬──────┬─┘ └┬──────┬─┘ └──┬─────┬───┘ └──┬─────┬──────┘
      │     │        │     │      │      │    │      │      │     │        │     │
   AUTH_  Redis   USR_  Redis  PRJ_  Redis TSK_  Redis  SOP_  Redis    NTF_  Redis
   SCHEMA        SCHEMA        SCHEMA      SCHEMA       SCHEMA         SCHEMA
      └───────────────┴─────────────┴──── Oracle DB ─────┴──────────────┘

   ════════════ Message Broker (RabbitMQ/Kafka) — event asinkron ════════════
   UserCreated, ProjectCreated, TaskAssigned, TaskStatusChanged,
   SopSubmitted, SopApproved, SopPublished  ──►  Notification, Audit, Search

   Observability: Serilog + OpenTelemetry ──► ELK / Loki + Tempo + Prometheus ──► Grafana
```

### A.2 Alur Komunikasi Antar Microservices

| Pola | Kapan dipakai | Contoh |
| --- | --- | --- |
| REST sinkron (via Gateway) | Frontend ke backend | `GET /api/projects/{id}` |
| REST/gRPC sinkron antar service | Butuh jawaban saat itu juga, harus cepat | Task Service cek keanggotaan proyek ke Project Service |
| Event asinkron (broker) | Memberi tahu tanpa menunggu | `TaskAssigned` → Notification Service kirim email |
| Outbox pattern | Menjamin event terkirim bersamaan dengan commit DB | Tabel `OUTBOX_MESSAGES` di tiap schema, di-poll oleh worker |

Aturan utama:

1. Tidak ada service yang membaca tabel schema lain secara langsung (no shared database).
2. Panggilan sinkron antar service dibungkus **Polly** (retry, timeout 2–3 detik, circuit breaker).
3. Data milik service lain yang sering dibaca disalin secara lokal sebagai *read model* (mis. Task Service menyimpan `PROJECT_REF` berisi id, nama, status proyek) dan diperbarui lewat event.
4. Setiap request membawa `X-Correlation-Id` dan `X-Tenant-Id` agar bisa dilacak end-to-end.

Contoh alur "Assign task":

1. React memanggil `PUT /api/tasks/{id}/assignee` ke Gateway.
2. Gateway memvalidasi JWT, cek rate limit di Redis, teruskan ke Task Service.
3. Task Service memvalidasi assignee adalah anggota proyek (read model lokal; fallback ke Project Service).
4. Task Service update `TSK_SCHEMA.TASKS` + insert `OUTBOX_MESSAGES` dalam satu transaksi Oracle.
5. Worker outbox mem-publish `TaskAssigned` ke broker; cache `task:{id}` di Redis dihapus.
6. Notification Service mengonsumsi event → simpan notifikasi → push via SignalR + kirim email.

### A.3 Redis sebagai Caching Layer

Redis dipakai dengan pola **cache-aside**: baca cache dulu, jika kosong ambil dari Oracle lalu simpan dengan TTL; saat data berubah, kunci cache dihapus (bukan di-update).

| Penggunaan | Kunci (contoh) | TTL |
| --- | --- | --- |
| Cache entitas | `tnt:{tenantId}:project:{id}` | 10 menit |
| Cache daftar/halaman | `tnt:{tenantId}:projects:list:{hash(query)}` | 2 menit |
| Permission user | `tnt:{tenantId}:perm:{userId}` | 15 menit |
| Refresh token / sesi | `auth:rt:{tokenId}` | sesuai masa refresh token (7 hari) |
| Token blacklist (logout) | `auth:revoked:{jti}` | sisa umur access token |
| Rate limit | `rl:{clientId}:{window}` | 1 menit |
| Distributed lock | `lock:sop:{sopId}:edit` | 5 menit (diperpanjang) |
| Jumlah notifikasi belum dibaca | `ntf:unread:{userId}` | tanpa TTL, dihitung ulang saat cache miss |

Praktik: prefix per tenant, serialisasi JSON ringkas (System.Text.Json), jangan cache data sensitif tanpa enkripsi, gunakan Redis Sentinel/Cluster untuk HA, dan siapkan *graceful degradation* (jika Redis mati, aplikasi tetap jalan langsung ke Oracle).

### A.4 Oracle sebagai Database Utama

- **Satu schema per service** (`AUTH_SCHEMA`, `USR_SCHEMA`, `PRJ_SCHEMA`, `TSK_SCHEMA`, `SOP_SCHEMA`, `NTF_SCHEMA`, `AUD_SCHEMA`) dengan user database terpisah dan hak akses hanya ke schema sendiri. Ini memberi isolasi seperti "database per service" tanpa biaya lisensi banyak instance.
- Koneksi dari .NET memakai **Oracle.ManagedDataAccess.Core** dengan connection pooling; kredensial disimpan di Kubernetes Secret/Vault.
- Migrasi skema memakai **Flyway** atau **Liquibase** (lebih matang untuk Oracle dibanding EF Migrations), dijalankan sebagai Kubernetes Job sebelum deploy.
- HA dan backup: Oracle Data Guard/RAC sesuai kebijakan perusahaan, RMAN backup harian, Flashback untuk pemulihan cepat.

### A.5 API Gateway dan Service Discovery

| Komponen | Pilihan | Catatan |
| --- | --- | --- |
| API Gateway | **YARP** (rekomendasi untuk tim .NET), alternatif Kong / Ocelot | Satu codebase .NET, mudah tambah middleware JWT & rate limit |
| Service discovery | **Kubernetes DNS** (`task-service.app.svc.cluster.local`) | Di Kubernetes tidak perlu Consul/Eureka |
| Di luar Kubernetes | Consul + health check | Untuk lingkungan VM/dev lokal |
| Health check | `/health/live` dan `/health/ready` (ASP.NET HealthChecks: Oracle, Redis, broker) | Dipakai liveness/readiness probe |

Contoh routing YARP (`appsettings.json` di Gateway):

```json
{
  "ReverseProxy": {
    "Routes": {
      "projects": { "ClusterId": "project", "Match": { "Path": "/api/projects/{**rest}" }, "AuthorizationPolicy": "default", "RateLimiterPolicy": "per-user" },
      "tasks":    { "ClusterId": "task",    "Match": { "Path": "/api/tasks/{**rest}" },    "AuthorizationPolicy": "default", "RateLimiterPolicy": "per-user" },
      "sops":     { "ClusterId": "sop",     "Match": { "Path": "/api/sops/{**rest}" },     "AuthorizationPolicy": "default", "RateLimiterPolicy": "per-user" },
      "auth":     { "ClusterId": "auth",    "Match": { "Path": "/api/auth/{**rest}" },     "RateLimiterPolicy": "login" }
    },
    "Clusters": {
      "project": { "Destinations": { "d1": { "Address": "http://project-service.app.svc.cluster.local" } } },
      "task":    { "Destinations": { "d1": { "Address": "http://task-service.app.svc.cluster.local" } } },
      "sop":     { "Destinations": { "d1": { "Address": "http://sop-service.app.svc.cluster.local" } } },
      "auth":    { "Destinations": { "d1": { "Address": "http://auth-service.app.svc.cluster.local" } } }
    }
  }
}
```

## B. Perencanaan Microservices

Enam service inti dipisah per bounded context; satu service tambahan (Audit) disarankan agar jejak audit tidak tercampur dengan logika bisnis. Setiap service punya schema Oracle sendiri, cache Redis dengan prefix sendiri, dan hanya berkomunikasi lewat API atau event.

### B.0 Peta Dependency

| Service | Memanggil (sinkron) | Mem-publish event | Mengonsumsi event |
| --- | --- | --- | --- |
| Auth | User (ambil role saat login) | `UserLoggedIn`, `TokenRevoked` | `UserDeactivated` |
| User | — | `UserCreated`, `UserUpdated`, `UserDeactivated`, `RoleChanged` | — |
| Project | User (validasi anggota) | `ProjectCreated`, `ProjectUpdated`, `MemberAdded`, `ProjectArchived` | `UserDeactivated` |
| Task | Project (cek keanggotaan, fallback) | `TaskCreated`, `TaskAssigned`, `TaskStatusChanged`, `TaskCommented` | `ProjectArchived`, `MemberRemoved`, `SopPublished` |
| SOP | User (cek role approver) | `SopSubmitted`, `SopApproved`, `SopRejected`, `SopPublished` | `UserDeactivated` |
| Notification | — | — | Hampir semua event di atas |
| Audit (opsional) | — | — | Semua event + log dari Gateway |

### B.1 Auth Service

- **Tanggung jawab:** login, penerbitan & refresh token (OAuth2/OIDC), logout/revocation, MFA, integrasi SSO korporat. Tidak menyimpan profil lengkap user.
- **Schema `AUTH_SCHEMA`:** `CREDENTIALS` (user\_id, password\_hash Argon2/BCrypt, failed\_attempts, locked\_until), `REFRESH_TOKENS` (token\_id, user\_id, hash, expires\_at, revoked\_at, device\_info), `CLIENTS` (OAuth client\_id, secret\_hash, redirect\_uris, scopes), `MFA_SECRETS`, `LOGIN_HISTORY`.
- **Endpoint:**

| Method | Path | Fungsi |
| --- | --- | --- |
| POST | `/api/auth/login` | Login, mengembalikan access token (15 menit) + refresh token (7 hari, httpOnly cookie) |
| POST | `/api/auth/refresh` | Rotasi refresh token |
| POST | `/api/auth/logout` | Revoke refresh token + blacklist `jti` access token |
| POST | `/api/auth/mfa/verify` | Verifikasi OTP |
| GET | `/.well-known/openid-configuration`, `/.well-known/jwks.json` | Discovery & public key untuk validasi JWT di service lain |

- **Alur data:** kredensial dicek → klaim role/permission diambil dari User Service (di-cache) → JWT ditandatangani RS256 → refresh token di-hash dan disimpan di Oracle + Redis.
- **Redis:** `auth:rt:{tokenId}`, `auth:revoked:{jti}`, `auth:loginfail:{username}` (lockout), `auth:otp:{userId}`.
- **Rekomendasi:** gunakan **Duende IdentityServer** atau **OpenIddict**, atau delegasikan ke **Keycloak/Azure AD** jika perusahaan sudah punya IdP. Jangan menulis kriptografi token sendiri.

### B.2 User Service

- **Tanggung jawab:** tenant (perusahaan), profil user, departemen, role, permission, keanggotaan tim.
- **Schema `USR_SCHEMA`:** `TENANTS`, `USERS`, `DEPARTMENTS`, `ROLES`, `PERMISSIONS`, `ROLE_PERMISSIONS`, `USER_ROLES`, `TEAMS`, `TEAM_MEMBERS`.
- **Endpoint:**

| Method | Path | Fungsi |
| --- | --- | --- |
| GET | `/api/users?search=&dept=&page=&size=` | Daftar user (paging) |
| GET | `/api/users/{id}` | Detail user |
| GET | `/api/users/me` | Profil user login |
| POST | `/api/users` | Buat user (admin) |
| PUT | `/api/users/{id}` | Ubah profil |
| PATCH | `/api/users/{id}/status` | Aktif/nonaktif |
| GET/POST | `/api/roles`, `/api/roles/{id}/permissions` | Kelola role & permission |
| PUT | `/api/users/{id}/roles` | Atur role user |
| GET | `/api/users/{id}/permissions` | Dipakai Auth/Gateway (internal) |

- **Alur data:** admin membuat user → simpan Oracle → publish `UserCreated` → Auth membuat kredensial awal dan Notification mengirim email undangan.
- **Redis:** `tnt:{t}:user:{id}` (10 menit), `tnt:{t}:perm:{userId}` (15 menit, dihapus saat `RoleChanged`), `tnt:{t}:roles` (1 jam).

### B.3 Project Service

- **Tanggung jawab:** proyek, anggota proyek beserta perannya di proyek, milestone, label.
- **Schema `PRJ_SCHEMA`:** `PROJECTS`, `PROJECT_MEMBERS`, `MILESTONES`, `PROJECT_LABELS`, `PROJECT_SOP_LINKS` (proyek ↔ SOP yang wajib diikuti), `USER_REF` (read model user), `OUTBOX_MESSAGES`.
- **Endpoint:**

| Method | Path | Fungsi |
| --- | --- | --- |
| GET | `/api/projects?status=&ownerId=&page=` | Daftar proyek |
| GET | `/api/projects/{id}` | Detail proyek |
| POST | `/api/projects` | Buat proyek |
| PUT | `/api/projects/{id}` | Ubah proyek |
| DELETE | `/api/projects/{id}` | Arsip (soft delete) |
| GET/POST/DELETE | `/api/projects/{id}/members[/{userId}]` | Kelola anggota |
| GET/POST/PUT | `/api/projects/{id}/milestones` | Kelola milestone |
| POST | `/api/projects/{id}/sops` | Tautkan SOP ke proyek |
| GET | `/api/projects/{id}/summary` | Ringkasan progres untuk dashboard |

- **Alur data:** buat proyek → pembuat otomatis jadi `PROJECT_MANAGER` → publish `ProjectCreated` → Task Service menyimpan `PROJECT_REF`.
- **Redis:** `tnt:{t}:project:{id}`, `tnt:{t}:project:{id}:members` (dipakai Task Service untuk cek akses), `tnt:{t}:project:{id}:summary` (1 menit, data dashboard).

### B.4 Task Service

- **Tanggung jawab:** tugas, subtask, assignee, status/workflow, komentar, lampiran, checklist (termasuk checklist yang dihasilkan dari SOP).
- **Schema `TSK_SCHEMA`:** `TASKS`, `TASK_ASSIGNEES`, `TASK_COMMENTS`, `TASK_ATTACHMENTS`, `TASK_CHECKLIST_ITEMS`, `TASK_STATUS_HISTORY`, `TASK_SOP_LINKS`, `PROJECT_REF`, `OUTBOX_MESSAGES`.
- **Endpoint:**

| Method | Path | Fungsi |
| --- | --- | --- |
| GET | `/api/tasks?projectId=&assigneeId=&status=&due_before=&page=` | Daftar/filter tugas |
| GET | `/api/tasks/{id}` | Detail tugas |
| POST | `/api/tasks` | Buat tugas |
| PUT | `/api/tasks/{id}` | Ubah tugas |
| PATCH | `/api/tasks/{id}/status` | Pindah status (validasi transisi) |
| PUT | `/api/tasks/{id}/assignees` | Atur assignee |
| GET/POST | `/api/tasks/{id}/comments` | Komentar |
| POST | `/api/tasks/{id}/attachments` | Upload (file ke object storage, metadata ke Oracle) |
| POST | `/api/tasks/{id}/apply-sop/{sopVersionId}` | Buat checklist dari langkah SOP |
| GET | `/api/tasks/my?status=` | Tugas milik user login |

- **Alur data:** lihat contoh "Assign task" di A.2. Transisi status mengikuti state machine `TODO → IN_PROGRESS → IN_REVIEW → DONE` (+ `BLOCKED`, `CANCELLED`).
- **Redis:** `tnt:{t}:task:{id}`, `tnt:{t}:tasks:my:{userId}` (1 menit), `tnt:{t}:board:{projectId}` (kanban, 30 detik), lock `lock:task:{id}` untuk update status bersamaan.

### B.5 SOP Management Service

- **Tanggung jawab:** kategori SOP, dokumen SOP, versi, langkah-langkah, alur review/approval, publikasi, bukti "sudah dibaca" (acknowledgement). Detail lengkap ada di bagian H.
- **Schema `SOP_SCHEMA`:** `SOP_CATEGORIES`, `SOP_DOCUMENTS`, `SOP_VERSIONS`, `SOP_STEPS`, `SOP_APPROVALS`, `SOP_APPROVAL_FLOWS`, `SOP_ACKNOWLEDGEMENTS`, `SOP_TEMPLATES`, `OUTBOX_MESSAGES`.
- **Endpoint:**

| Method | Path | Fungsi |
| --- | --- | --- |
| GET | `/api/sops?category=&status=&q=` | Daftar & cari SOP |
| GET | `/api/sops/{id}` | Detail SOP + versi aktif |
| POST | `/api/sops` | Buat SOP baru (draft v0.1) |
| PUT | `/api/sops/{id}/versions/{versionId}` | Simpan draft |
| POST | `/api/sops/{id}/versions` | Buat versi baru dari versi published |
| POST | `/api/sops/{id}/versions/{versionId}/submit` | Ajukan review |
| POST | `/api/sops/{id}/versions/{versionId}/approve` atau `/reject` | Keputusan reviewer/approver |
| POST | `/api/sops/{id}/versions/{versionId}/publish` | Terbitkan |
| GET | `/api/sops/{id}/versions` dan `/versions/{a}/diff/{b}` | Riwayat & perbandingan versi |
| POST | `/api/sops/{id}/acknowledge` | User menyatakan sudah membaca |

- **Alur data:** draft → submit → approval berjenjang → publish → event `SopPublished` → Notification memberi tahu pihak terkait, Task Service memperbarui checklist yang tertaut.
- **Redis:** `tnt:{t}:sop:{id}:published` (30 menit, karena jarang berubah), `tnt:{t}:sop:categories`, lock edit `lock:sop:{id}:edit`, autosave sementara `sop:draft:{versionId}:{userId}`.

### B.6 Notification Service

- **Tanggung jawab:** mengubah event menjadi notifikasi in-app, email, (opsional) WhatsApp/Teams; preferensi notifikasi per user; real-time push.
- **Schema `NTF_SCHEMA`:** `NOTIFICATIONS`, `NOTIFICATION_PREFERENCES`, `NOTIFICATION_TEMPLATES`, `EMAIL_QUEUE`, `DELIVERY_LOGS`.
- **Endpoint:**

| Method | Path | Fungsi |
| --- | --- | --- |
| GET | `/api/notifications?unreadOnly=&page=` | Daftar notifikasi |
| GET | `/api/notifications/unread-count` | Badge jumlah belum dibaca |
| PATCH | `/api/notifications/{id}/read`, `/read-all` | Tandai dibaca |
| GET/PUT | `/api/notifications/preferences` | Preferensi kanal |
| WS | `/hubs/notifications` | SignalR hub untuk push real-time |

- **Alur data:** konsumsi event → cek preferensi → render template → simpan → push SignalR (backplane Redis agar bisa multi-pod) → kirim email via SMTP/SendGrid dengan retry.
- **Redis:** `ntf:unread:{userId}`, SignalR backplane (pub/sub), idempotency key `ntf:evt:{eventId}` (24 jam) agar event yang terkirim dua kali tidak menghasilkan notifikasi ganda.

## C. Perencanaan Database Oracle

Setiap tabel transaksional punya `TENANT_ID`, kolom audit standar, dan primary key `RAW(16)` (GUID) atau `NUMBER` dari `IDENTITY`; relasi lintas schema tidak memakai foreign key fisik, cukup ID referensi yang dijaga lewat event.

### C.1 Konvensi

| Aspek | Konvensi |
| --- | --- |
| Nama | `UPPER_SNAKE_CASE`, tabel jamak (`TASKS`), PK `ID`, FK `<ENTITAS>_ID` |
| Primary key | `NUMBER GENERATED ALWAYS AS IDENTITY` untuk tabel internal; `PUBLIC_ID RAW(16) DEFAULT SYS_GUID()` untuk ID yang terekspos ke API |
| Kolom audit | `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`, `ROW_VERSION` (optimistic concurrency), `IS_DELETED` (soft delete) |
| Waktu | `TIMESTAMP WITH TIME ZONE`, disimpan UTC |
| Teks panjang | `CLOB` (konten SOP), JSON native (`JSON` di 21c+, atau `CLOB CHECK (... IS JSON)` di 19c) |
| Boolean | `NUMBER(1) CHECK (col IN (0,1))` (atau `BOOLEAN` di 23ai) |
| Multi-tenant | `TENANT_ID` di setiap tabel + Virtual Private Database (VPD) policy sebagai pengaman lapis kedua |

### C.2 Struktur Tabel Utama

**USR\_SCHEMA**

| Tabel | Kolom utama |
| --- | --- |
| `TENANTS` | ID, CODE (unik), NAME, PLAN, STATUS, SETTINGS (JSON) |
| `USERS` | ID, PUBLIC\_ID, TENANT\_ID, EMAIL (unik per tenant), FULL\_NAME, DEPARTMENT\_ID, JOB\_TITLE, PHONE, STATUS, LAST\_LOGIN\_AT |
| `DEPARTMENTS` | ID, TENANT\_ID, NAME, PARENT\_ID (hierarki), HEAD\_USER\_ID |
| `ROLES` | ID, TENANT\_ID, CODE, NAME, IS\_SYSTEM |
| `PERMISSIONS` | ID, CODE (mis. `sop.approve`), MODULE, DESCRIPTION |
| `ROLE_PERMISSIONS` | ROLE\_ID, PERMISSION\_ID |
| `USER_ROLES` | USER\_ID, ROLE\_ID, SCOPE\_TYPE (GLOBAL/DEPARTMENT/PROJECT), SCOPE\_ID |

**PRJ\_SCHEMA**

| Tabel | Kolom utama |
| --- | --- |
| `PROJECTS` | ID, PUBLIC\_ID, TENANT\_ID, CODE, NAME, DESCRIPTION, OWNER\_ID, DEPARTMENT\_ID, STATUS, PRIORITY, START\_DATE, END\_DATE, PROGRESS\_PCT |
| `PROJECT_MEMBERS` | PROJECT\_ID, USER\_ID, PROJECT\_ROLE (MANAGER/MEMBER/VIEWER), JOINED\_AT |
| `MILESTONES` | ID, PROJECT\_ID, NAME, DUE\_DATE, STATUS |
| `PROJECT_SOP_LINKS` | PROJECT\_ID, SOP\_ID, IS\_MANDATORY |

**TSK\_SCHEMA**

| Tabel | Kolom utama |
| --- | --- |
| `TASKS` | ID, PUBLIC\_ID, TENANT\_ID, PROJECT\_ID, MILESTONE\_ID, PARENT\_TASK\_ID, TITLE, DESCRIPTION (CLOB), STATUS, PRIORITY, DUE\_DATE, ESTIMATE\_HOURS, REPORTER\_ID, COMPLETED\_AT |
| `TASK_ASSIGNEES` | TASK\_ID, USER\_ID, ASSIGNED\_AT, ASSIGNED\_BY |
| `TASK_COMMENTS` | ID, TASK\_ID, AUTHOR\_ID, BODY (CLOB), CREATED\_AT |
| `TASK_ATTACHMENTS` | ID, TASK\_ID, FILE\_NAME, STORAGE\_KEY, SIZE\_BYTES, MIME\_TYPE |
| `TASK_CHECKLIST_ITEMS` | ID, TASK\_ID, SOP\_STEP\_ID (nullable), TEXT, IS\_DONE, DONE\_BY, DONE\_AT, SORT\_ORDER |
| `TASK_STATUS_HISTORY` | ID, TASK\_ID, FROM\_STATUS, TO\_STATUS, CHANGED\_BY, CHANGED\_AT |

**SOP\_SCHEMA**

| Tabel | Kolom utama |
| --- | --- |
| `SOP_CATEGORIES` | ID, TENANT\_ID, NAME, PARENT\_ID |
| `SOP_DOCUMENTS` | ID, PUBLIC\_ID, TENANT\_ID, CODE (mis. `SOP-HR-001`), TITLE, CATEGORY\_ID, OWNER\_DEPT\_ID, CURRENT\_VERSION\_ID, STATUS |
| `SOP_VERSIONS` | ID, SOP\_ID, VERSION\_MAJOR, VERSION\_MINOR (tampil sebagai 1.0, 1.1), STATUS, CONTENT\_JSON (CLOB), CONTENT\_HTML (CLOB), CHANGE\_SUMMARY, EFFECTIVE\_DATE, REVIEW\_DUE\_DATE, PUBLISHED\_AT, PUBLISHED\_BY |
| `SOP_STEPS` | ID, SOP\_VERSION\_ID, STEP\_NO, TITLE, DESCRIPTION, RESPONSIBLE\_ROLE, IS\_MANDATORY |
| `SOP_APPROVAL_FLOWS` | ID, TENANT\_ID, CATEGORY\_ID, LEVEL\_NO, APPROVER\_ROLE\_ID / APPROVER\_USER\_ID |
| `SOP_APPROVALS` | ID, SOP\_VERSION\_ID, LEVEL\_NO, APPROVER\_ID, DECISION, NOTE, DECIDED\_AT |
| `SOP_ACKNOWLEDGEMENTS` | SOP\_VERSION\_ID, USER\_ID, ACKNOWLEDGED\_AT |

**AUD\_SCHEMA**: `AUDIT_LOGS` (ID, TENANT\_ID, OCCURRED\_AT, ACTOR\_ID, ACTION, ENTITY\_TYPE, ENTITY\_ID, OLD\_VALUES JSON, NEW\_VALUES JSON, IP\_ADDRESS, USER\_AGENT, CORRELATION\_ID).

### C.3 ERD Deskriptif

```
TENANTS 1 ──< USERS >── USER_ROLES ──< ROLES >── ROLE_PERMISSIONS ──< PERMISSIONS
   │            │
   │            └──< DEPARTMENTS (PARENT_ID → self)
   │
   ├──< PROJECTS 1 ──< PROJECT_MEMBERS >── (USER_ID → USERS)
   │       │  └──< MILESTONES
   │       └──< PROJECT_SOP_LINKS >── (SOP_ID → SOP_DOCUMENTS)
   │
   ├──< TASKS (PROJECT_ID → PROJECTS, PARENT_TASK_ID → self)
   │       ├──< TASK_ASSIGNEES >── (USER_ID → USERS)
   │       ├──< TASK_COMMENTS
   │       ├──< TASK_ATTACHMENTS
   │       ├──< TASK_STATUS_HISTORY
   │       └──< TASK_CHECKLIST_ITEMS >── (SOP_STEP_ID → SOP_STEPS)
   │
   └──< SOP_DOCUMENTS 1 ──< SOP_VERSIONS 1 ──< SOP_STEPS
           │   (CURRENT_VERSION_ID → SOP_VERSIONS)
           │                 ├──< SOP_APPROVALS
           │                 └──< SOP_ACKNOWLEDGEMENTS
           └── CATEGORY_ID → SOP_CATEGORIES ──< SOP_APPROVAL_FLOWS

Keterangan: 1 ──< = one-to-many. Garis dalam satu schema = FK fisik;
referensi lintas schema (USER_ID, PROJECT_ID, SOP_ID) = FK logis.
```

### C.4 Contoh DDL

```sql
-- TSK_SCHEMA
CREATE TABLE TASKS (
  ID              NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PUBLIC_ID       RAW(16) DEFAULT SYS_GUID() NOT NULL,
  TENANT_ID       NUMBER NOT NULL,
  PROJECT_ID      NUMBER NOT NULL,
  PARENT_TASK_ID  NUMBER REFERENCES TASKS(ID),
  TITLE           VARCHAR2(300 CHAR) NOT NULL,
  DESCRIPTION     CLOB,
  STATUS          VARCHAR2(20) DEFAULT 'TODO' NOT NULL
                  CHECK (STATUS IN ('TODO','IN_PROGRESS','IN_REVIEW','BLOCKED','DONE','CANCELLED')),
  PRIORITY        VARCHAR2(10) DEFAULT 'MEDIUM' NOT NULL
                  CHECK (PRIORITY IN ('LOW','MEDIUM','HIGH','URGENT')),
  DUE_DATE        DATE,
  REPORTER_ID     NUMBER NOT NULL,
  COMPLETED_AT    TIMESTAMP WITH TIME ZONE,
  CREATED_AT      TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
  CREATED_BY      NUMBER NOT NULL,
  UPDATED_AT      TIMESTAMP WITH TIME ZONE,
  UPDATED_BY      NUMBER,
  ROW_VERSION     NUMBER DEFAULT 1 NOT NULL,
  IS_DELETED      NUMBER(1) DEFAULT 0 NOT NULL CHECK (IS_DELETED IN (0,1)),
  CONSTRAINT UQ_TASKS_PUBLIC_ID UNIQUE (PUBLIC_ID)
);

CREATE TABLE TASK_ASSIGNEES (
  TASK_ID      NUMBER NOT NULL REFERENCES TASKS(ID) ON DELETE CASCADE,
  USER_ID      NUMBER NOT NULL,
  ASSIGNED_AT  TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
  ASSIGNED_BY  NUMBER NOT NULL,
  CONSTRAINT PK_TASK_ASSIGNEES PRIMARY KEY (TASK_ID, USER_ID)
);

-- SOP_SCHEMA
CREATE TABLE SOP_VERSIONS (
  ID              NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  SOP_ID          NUMBER NOT NULL REFERENCES SOP_DOCUMENTS(ID),
  VERSION_MAJOR   NUMBER(3) NOT NULL,
  VERSION_MINOR   NUMBER(3) NOT NULL,
  STATUS          VARCHAR2(20) NOT NULL
                  CHECK (STATUS IN ('DRAFT','IN_REVIEW','APPROVED','REJECTED','PUBLISHED','SUPERSEDED','ARCHIVED')),
  CONTENT_JSON    CLOB CHECK (CONTENT_JSON IS JSON),
  CONTENT_HTML    CLOB,
  CHANGE_SUMMARY  VARCHAR2(1000 CHAR),
  EFFECTIVE_DATE  DATE,
  REVIEW_DUE_DATE DATE,
  PUBLISHED_AT    TIMESTAMP WITH TIME ZONE,
  PUBLISHED_BY    NUMBER,
  CREATED_AT      TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
  CREATED_BY      NUMBER NOT NULL,
  ROW_VERSION     NUMBER DEFAULT 1 NOT NULL,
  CONSTRAINT UQ_SOP_VERSION UNIQUE (SOP_ID, VERSION_MAJOR, VERSION_MINOR)
);

-- Hanya boleh ada satu versi PUBLISHED per SOP (function-based unique index)
CREATE UNIQUE INDEX UX_SOP_ONE_PUBLISHED
  ON SOP_VERSIONS (CASE WHEN STATUS = 'PUBLISHED' THEN SOP_ID END);
```

### C.5 Strategi Indexing

| Index | Tujuan |
| --- | --- |
| `IX_TASKS_TNT_PRJ_STATUS (TENANT_ID, PROJECT_ID, STATUS, IS_DELETED)` | Kanban board & filter per proyek |
| `IX_TASK_ASSIGNEES_USER (USER_ID, TASK_ID)` | "Tugas saya" |
| `IX_TASKS_DUE (TENANT_ID, DUE_DATE) WHERE` status aktif (via function-based index) | Pengingat tenggat |
| `IX_PROJECTS_TNT_STATUS (TENANT_ID, STATUS)` | Daftar proyek |
| `UX_USERS_TNT_EMAIL (TENANT_ID, LOWER(EMAIL))` | Login & cegah email ganda (function-based) |
| `IX_SOP_DOCS_TNT_CAT (TENANT_ID, CATEGORY_ID, STATUS)` | Daftar SOP per kategori |
| Oracle Text `CTXSYS.CONTEXT` pada `SOP_VERSIONS.CONTENT_HTML` dan `TASKS.TITLE` | Full-text search |
| `IX_AUDIT_TNT_ENTITY (TENANT_ID, ENTITY_TYPE, ENTITY_ID, OCCURRED_AT)` LOCAL | Riwayat audit per entitas |

Prinsip: index disusun dari kolom paling selektif yang selalu dipakai di `WHERE` (biasanya `TENANT_ID` di depan), semua FK diberi index untuk menghindari lock saat delete parent, hindari index berlebih di tabel tulis-berat (audit, notifikasi), dan pantau dengan AWR/`DBMS_XPLAN` sebelum menambah index baru.

### C.6 Partitioning

Partitioning memerlukan lisensi Oracle Partitioning (Enterprise Edition); terapkan hanya pada tabel yang tumbuh cepat.

| Tabel | Strategi | Alasan |
| --- | --- | --- |
| `AUDIT_LOGS` | Interval-range per bulan pada `OCCURRED_AT` | Volume tertinggi; partisi lama bisa dikompres/diarsip/drop sesuai retensi |
| `NOTIFICATIONS`, `DELIVERY_LOGS` | Interval-range per bulan | Data lama jarang dibaca, purge mudah |
| `TASK_STATUS_HISTORY` | Interval-range per kuartal | Riwayat panjang |
| `TASKS` | Opsional: LIST per `TENANT_ID` (tenant besar) atau HASH | Hanya bila > puluhan juta baris |

```sql
CREATE TABLE AUDIT_LOGS (
  ID              NUMBER GENERATED ALWAYS AS IDENTITY,
  TENANT_ID       NUMBER NOT NULL,
  OCCURRED_AT     TIMESTAMP NOT NULL,
  ACTOR_ID        NUMBER,
  ACTION          VARCHAR2(50) NOT NULL,
  ENTITY_TYPE     VARCHAR2(50) NOT NULL,
  ENTITY_ID       VARCHAR2(64) NOT NULL,
  OLD_VALUES      CLOB CHECK (OLD_VALUES IS JSON),
  NEW_VALUES      CLOB CHECK (NEW_VALUES IS JSON),
  IP_ADDRESS      VARCHAR2(45),
  CORRELATION_ID  VARCHAR2(64),
  CONSTRAINT PK_AUDIT_LOGS PRIMARY KEY (ID, OCCURRED_AT)
)
PARTITION BY RANGE (OCCURRED_AT)
INTERVAL (NUMTOYMINTERVAL(1, 'MONTH'))
(PARTITION P_INIT VALUES LESS THAN (TIMESTAMP '2026-01-01 00:00:00'));

CREATE INDEX IX_AUDIT_TNT_ENTITY
  ON AUDIT_LOGS (TENANT_ID, ENTITY_TYPE, ENTITY_ID, OCCURRED_AT) LOCAL;
```

## D. Perencanaan Backend (.NET)

Setiap service memakai **Clean Architecture** dengan sentuhan **DDD** di lapisan Domain dan **CQRS ringan** (MediatR): perintah tulis lewat EF Core/Dapper dengan transaksi, query baca lewat Dapper agar SQL Oracle bisa dioptimalkan. Kode bersama (logging, auth, outbox, Redis) dikemas sebagai paket NuGet internal `Company.BuildingBlocks.*`.

### D.1 Struktur Folder (per Service)

```
src/
├── BuildingBlocks/                      # paket NuGet internal, dipakai semua service
│   ├── Company.BuildingBlocks.Common/     # Result<T>, PagedResult, exceptions
│   ├── Company.BuildingBlocks.Auth/       # JWT setup, permission policy
│   ├── Company.BuildingBlocks.Caching/    # ICacheService + Redis
│   ├── Company.BuildingBlocks.Messaging/  # MassTransit + Outbox
│   └── Company.BuildingBlocks.Observability/ # Serilog, OpenTelemetry
├── ApiGateway/
│   └── Gateway.Api/                      # YARP + rate limiting
└── Services/
    └── Task/
        ├── Task.Domain/                  # inti bisnis, tanpa dependency luar
        │   ├── Entities/ (TaskItem.cs, TaskComment.cs)
        │   ├── ValueObjects/ (TaskStatus.cs, Priority.cs)
        │   ├── Events/ (TaskAssignedDomainEvent.cs)
        │   ├── Exceptions/
        │   └── Repositories/ (ITaskRepository.cs)  # interface saja
        ├── Task.Application/             # use case
        │   ├── Common/ (IUnitOfWork.cs, ICurrentUser.cs, Behaviors/)
        │   ├── Tasks/
        │   │   ├── Commands/ (CreateTask/, UpdateTask/, ChangeStatus/, AssignTask/)
        │   │   ├── Queries/ (GetTaskById/, GetTasks/, GetMyTasks/)
        │   │   └── Dtos/ (TaskDto.cs)
        │   └── DependencyInjection.cs
        ├── Task.Infrastructure/          # Oracle, Redis, broker, HTTP client
        │   ├── Persistence/ (TaskDbContext.cs, Configurations/, Repositories/, Migrations-sql/)
        │   ├── Caching/
        │   ├── Messaging/ (Consumers/, Outbox/)
        │   ├── ExternalServices/ (ProjectServiceClient.cs)
        │   └── DependencyInjection.cs
        ├── Task.Api/                     # presentasi
        │   ├── Controllers/ (TasksController.cs)
        │   ├── Middleware/ (ExceptionHandlingMiddleware.cs, TenantMiddleware.cs)
        │   ├── Program.cs
        │   ├── appsettings.json
        │   └── Dockerfile
        └── tests/
            ├── Task.Domain.Tests/
            ├── Task.Application.Tests/
            └── Task.Api.IntegrationTests/   # Testcontainers: Oracle Free + Redis
```

### D.2 Pola Arsitektur

| Lapisan | Isi | Boleh bergantung pada |
| --- | --- | --- |
| Domain | Entity, value object, domain event, aturan bisnis (mis. transisi status) | Tidak ada |
| Application | Command/query handler, validasi (FluentValidation), DTO, interface port | Domain |
| Infrastructure | Implementasi repository Oracle, Redis, broker, HTTP client | Application, Domain |
| Api | Controller/Minimal API, middleware, konfigurasi DI | Application, Infrastructure (hanya untuk wiring) |

Rekomendasi: Clean Architecture + DDD taktis (aggregate, domain event) sudah cukup; Onion pada dasarnya varian yang sama. Hindari over-engineering: service sederhana seperti Notification boleh memakai struktur lebih tipis (*vertical slice*).

### D.3 Entity Domain (C#)

```csharp
namespace Task.Domain.Entities;

public sealed class TaskItem : AggregateRoot
{
    private readonly List<long> _assigneeIds = new();

    public long TenantId { get; private set; }
    public long ProjectId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public TaskStatus Status { get; private set; } = TaskStatus.Todo;
    public Priority Priority { get; private set; } = Priority.Medium;
    public DateOnly? DueDate { get; private set; }
    public IReadOnlyCollection<long> AssigneeIds => _assigneeIds;

    private TaskItem() { } // untuk EF Core

    public static TaskItem Create(long tenantId, long projectId, string title,
        Priority priority, DateOnly? dueDate, long reporterId)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Judul tugas wajib diisi.");

        var task = new TaskItem
        {
            TenantId = tenantId, ProjectId = projectId, Title = title.Trim(),
            Priority = priority, DueDate = dueDate, CreatedBy = reporterId
        };
        task.AddDomainEvent(new TaskCreatedDomainEvent(task.PublicId, projectId));
        return task;
    }

    public void ChangeStatus(TaskStatus next, long actorId)
    {
        if (!Status.CanTransitionTo(next))
            throw new DomainException($"Transisi {Status} → {next} tidak diizinkan.");

        var previous = Status;
        Status = next;
        AddDomainEvent(new TaskStatusChangedDomainEvent(PublicId, previous, next, actorId));
    }

    public void Assign(long userId, long actorId)
    {
        if (_assigneeIds.Contains(userId)) return;
        _assigneeIds.Add(userId);
        AddDomainEvent(new TaskAssignedDomainEvent(PublicId, userId, actorId));
    }
}
```

### D.4 Repository Pattern untuk Oracle

Dua opsi akses data yang bisa dipadukan: **EF Core + `Oracle.EntityFrameworkCore`** untuk perintah tulis (change tracking, transaksi) dan **Dapper + `Oracle.ManagedDataAccess.Core`** untuk query baca yang kompleks.

```csharp
// Task.Domain/Repositories/ITaskRepository.cs
public interface ITaskRepository
{
    Task<TaskItem?> GetByPublicIdAsync(Guid publicId, CancellationToken ct);
    Task AddAsync(TaskItem task, CancellationToken ct);
    void Update(TaskItem task);
}

// Task.Infrastructure/Persistence/TaskDbContext.cs
public sealed class TaskDbContext : DbContext, IUnitOfWork
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public TaskDbContext(DbContextOptions<TaskDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("TSK_SCHEMA");
        b.Entity<TaskItem>(e =>
        {
            e.ToTable("TASKS");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.PublicId).HasColumnName("PUBLIC_ID");
            e.Property(x => x.Title).HasColumnName("TITLE").HasMaxLength(300);
            e.Property(x => x.Status).HasColumnName("STATUS").HasConversion<string>();
            e.Property(x => x.RowVersion).HasColumnName("ROW_VERSION").IsConcurrencyToken();
            e.HasQueryFilter(x => !x.IsDeleted);   // soft delete
        });
    }

    // Simpan domain event sebagai outbox dalam transaksi yang sama
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var events = ChangeTracker.Entries<AggregateRoot>()
            .SelectMany(e => e.Entity.PopDomainEvents()).ToList();
        Outbox.AddRange(events.Select(OutboxMessage.From));
        return await base.SaveChangesAsync(ct);
    }
}

// Task.Infrastructure/Persistence/Repositories/TaskRepository.cs
public sealed class TaskRepository(TaskDbContext db) : ITaskRepository
{
    public Task<TaskItem?> GetByPublicIdAsync(Guid publicId, CancellationToken ct) =>
        db.Tasks.FirstOrDefaultAsync(t => t.PublicId == publicId, ct);

    public async Task AddAsync(TaskItem task, CancellationToken ct) =>
        await db.Tasks.AddAsync(task, ct);

    public void Update(TaskItem task) => db.Tasks.Update(task);
}

// Query baca dengan Dapper (paging gaya Oracle 12c+)
public sealed class TaskReadQueries(IOracleConnectionFactory factory)
{
    public async Task<PagedResult<TaskDto>> GetByProjectAsync(
        long tenantId, long projectId, string? status, int page, int size, CancellationToken ct)
    {
        const string sql = """
            SELECT RAWTOHEX(t.PUBLIC_ID) AS Id, t.TITLE AS Title, t.STATUS AS Status,
                   t.PRIORITY AS Priority, t.DUE_DATE AS DueDate,
                   COUNT(*) OVER () AS TotalCount
              FROM TSK_SCHEMA.TASKS t
             WHERE t.TENANT_ID = :tenantId
               AND t.PROJECT_ID = :projectId
               AND t.IS_DELETED = 0
               AND (:status IS NULL OR t.STATUS = :status)
             ORDER BY t.DUE_DATE NULLS LAST, t.ID
             OFFSET :skip ROWS FETCH NEXT :take ROWS ONLY
            """;

        await using var conn = factory.Create();
        var rows = (await conn.QueryAsync<TaskRow>(new CommandDefinition(sql,
            new { tenantId, projectId, status, skip = (page - 1) * size, take = size },
            cancellationToken: ct))).ToList();

        return new PagedResult<TaskDto>(
            rows.Select(r => r.ToDto()).ToList(), page, size, rows.FirstOrDefault()?.TotalCount ?? 0);
    }
}
```

Catatan Oracle: selalu pakai bind variable (`:param`) agar aman dari SQL injection dan cursor bisa di-reuse; set `BindByName = true` pada `OracleCommand` (Dapper sudah melakukannya lewat parameter bernama); pakai `FetchSize` lebih besar untuk query besar.

### D.5 Konfigurasi Redis

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Oracle": "User Id=TSK_APP;Password=${ORACLE_PASSWORD};Data Source=oracle-db:1521/APPPDB;Pooling=true;Min Pool Size=5;Max Pool Size=100;",
    "Redis": "redis-master:6379,password=${REDIS_PASSWORD},ssl=false,abortConnect=false,connectTimeout=5000"
  },
  "Cache": { "DefaultTtlMinutes": 10, "KeyPrefix": "tsk:" }
}
```

```csharp
// Company.BuildingBlocks.Caching
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default);
}

public sealed class RedisCacheService(IConnectionMultiplexer redis, IOptions<CacheOptions> opt,
    ILogger<RedisCacheService> log) : ICacheService
{
    private readonly IDatabase _db = redis.GetDatabase();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _db.StringGetAsync(opt.Value.KeyPrefix + key);
            return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value!, Json);
        }
        catch (RedisException ex)          // graceful degradation
        {
            log.LogWarning(ex, "Redis GET gagal untuk {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        try
        {
            await _db.StringSetAsync(opt.Value.KeyPrefix + key,
                JsonSerializer.Serialize(value, Json),
                ttl ?? TimeSpan.FromMinutes(opt.Value.DefaultTtlMinutes));
        }
        catch (RedisException ex) { log.LogWarning(ex, "Redis SET gagal untuk {Key}", key); }
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        _db.KeyDeleteAsync(opt.Value.KeyPrefix + key);

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;
        var fresh = await factory();
        if (fresh is not null) await SetAsync(key, fresh, ttl, ct);
        return fresh;
    }
}

// Program.cs
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection("Cache"));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddSingleton<ICacheService, RedisCacheService>();
```

Alternatif: .NET 9 menyediakan **`HybridCache`** (cache memori lokal + Redis, dengan proteksi *cache stampede*) yang layak dipertimbangkan bila tim sudah di .NET 9+.

### D.6 Contoh Endpoint CRUD

```csharp
// Application: command + validator + handler
public sealed record CreateTaskCommand(Guid ProjectId, string Title, string? Description,
    Priority Priority, DateOnly? DueDate, IReadOnlyList<Guid> AssigneeIds) : IRequest<Guid>;

public sealed class CreateTaskValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(20_000);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => x.DueDate.HasValue);
        RuleFor(x => x.AssigneeIds).Must(a => a.Count <= 20);
    }
}

public sealed class CreateTaskHandler(ITaskRepository repo, IUnitOfWork uow,
    IProjectAccessChecker access, ICurrentUser user, ICacheService cache)
    : IRequestHandler<CreateTaskCommand, Guid>
{
    public async Task<Guid> Handle(CreateTaskCommand cmd, CancellationToken ct)
    {
        var projectId = await access.EnsureMemberAsync(cmd.ProjectId, user.Id, ct); // 403 jika bukan anggota
        var task = TaskItem.Create(user.TenantId, projectId, cmd.Title, cmd.Priority, cmd.DueDate, user.Id);
        foreach (var a in cmd.AssigneeIds)
            task.Assign(await access.ResolveMemberIdAsync(projectId, a, ct), user.Id);

        await repo.AddAsync(task, ct);
        await uow.SaveChangesAsync(ct);                                  // + outbox, satu transaksi
        await cache.RemoveAsync($"tnt:{user.TenantId}:board:{projectId}", ct);
        return task.PublicId;
    }
}

// Api: controller
[ApiController]
[Route("api/tasks")]
[Authorize]
public sealed class TasksController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "task.read")]
    public async Task<ActionResult<PagedResult<TaskDto>>> List([FromQuery] GetTasksQuery q, CancellationToken ct)
        => Ok(await mediator.Send(q, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "task.read")]
    public async Task<ActionResult<TaskDto>> Get(Guid id, CancellationToken ct)
        => await mediator.Send(new GetTaskByIdQuery(id), ct) is { } dto ? Ok(dto) : NotFound();

    [HttpPost]
    [Authorize(Policy = "task.create")]
    public async Task<IActionResult> Create(CreateTaskCommand cmd, CancellationToken ct)
    {
        var id = await mediator.Send(cmd, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "task.update")]
    public async Task<IActionResult> Update(Guid id, UpdateTaskRequest body,
        [FromHeader(Name = "If-Match")] int rowVersion, CancellationToken ct)
    {
        await mediator.Send(body.ToCommand(id, rowVersion), ct);   // 409 jika ROW_VERSION berbeda
        return NoContent();
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "task.update")]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeStatusRequest body, CancellationToken ct)
    {
        await mediator.Send(new ChangeTaskStatusCommand(id, body.Status), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "task.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeleteTaskCommand(id), ct);        // soft delete
        return NoContent();
    }
}
```

Error dikembalikan dalam format **RFC 7807 ProblemDetails** oleh `ExceptionHandlingMiddleware`: `ValidationException` → 400, `DomainException` → 422, `ForbiddenException` → 403, `NotFoundException` → 404, `DbUpdateConcurrencyException` → 409.

## E. Perencanaan Frontend (React)

Frontend adalah SPA React 18 + TypeScript (build dengan Vite), disusun **per fitur** agar sejalan dengan microservice di backend. State dibagi dua: **TanStack Query** untuk data dari server (cache, refetch, paging) dan **Zustand** untuk state UI/klien (sesi, tema, filter). Redux Toolkit tetap valid bila tim sudah terbiasa, tapi untuk kebutuhan ini Zustand + TanStack Query lebih ringkas.

### E.1 Struktur Folder

```
web/
├── public/
├── src/
│   ├── app/
│   │   ├── App.tsx
│   │   ├── router.tsx             # definisi route + lazy loading
│   │   ├── providers.tsx          # QueryClient, Theme, Auth
│   │   └── layouts/ (MainLayout.tsx, AuthLayout.tsx)
│   ├── features/
│   │   ├── auth/ (api/, components/LoginForm.tsx, hooks/useAuth.ts, store/authStore.ts)
│   │   ├── dashboard/ (pages/DashboardPage.tsx, components/StatCard.tsx, TaskSummaryChart.tsx)
│   │   ├── projects/ (api/, pages/ProjectListPage.tsx, ProjectDetailPage.tsx, components/, hooks/)
│   │   ├── tasks/ (api/, pages/TaskBoardPage.tsx, MyTasksPage.tsx, components/TaskCard.tsx, TaskDrawer.tsx)
│   │   ├── sops/ (api/, pages/SopListPage.tsx, SopEditorPage.tsx, SopViewPage.tsx, SopApprovalPage.tsx,
│   │   │          components/SopEditor.tsx, VersionHistory.tsx, DiffViewer.tsx)
│   │   ├── users/ (api/, pages/UserListPage.tsx, RoleManagementPage.tsx, components/)
│   │   └── notifications/ (api/, components/NotificationBell.tsx, hooks/useNotificationHub.ts)
│   ├── shared/
│   │   ├── api/ (httpClient.ts, queryClient.ts)
│   │   ├── components/ (DataTable, Modal, Can.tsx, PageHeader, EmptyState)
│   │   ├── hooks/ (usePermission.ts, useDebounce.ts)
│   │   ├── lib/ (formatDate.ts, validators.ts)
│   │   └── types/
│   ├── styles/
│   └── main.tsx
├── .env.example                 # VITE_API_BASE_URL=/api
├── Dockerfile
├── nginx.conf
└── vite.config.ts
```

Pustaka pendukung: **React Hook Form + Zod** (form & validasi), **Ant Design** atau **MUI** (komponen enterprise: tabel, form, date picker), **TipTap** (editor SOP), **dnd-kit** (kanban), **Recharts** (grafik dashboard), **@microsoft/signalr** (notifikasi real-time).

### E.2 Routing dan Proteksi Akses

| Path | Halaman | Permission |
| --- | --- | --- |
| `/login` | Login | publik |
| `/` | Dashboard | login |
| `/projects`, `/projects/:id` | Daftar & detail proyek | `project.read` |
| `/projects/:id/board` | Kanban tugas | `task.read` |
| `/tasks/my` | Tugas saya | login |
| `/sops`, `/sops/:id` | Daftar & baca SOP | `sop.read` |
| `/sops/new`, `/sops/:id/edit` | Editor SOP | `sop.create` / `sop.edit` |
| `/sops/approvals` | Antrian approval | `sop.approve` |
| `/admin/users`, `/admin/roles` | User & role management | `user.manage` |

```tsx
// src/app/router.tsx
const DashboardPage = lazy(() => import('@/features/dashboard/pages/DashboardPage'));
const SopEditorPage = lazy(() => import('@/features/sops/pages/SopEditorPage'));
// ...

function RequireAuth({ permission }: { permission?: string }) {
  const { isAuthenticated, hasPermission } = useAuthStore();
  const location = useLocation();
  if (!isAuthenticated) return <Navigate to="/login" state={{ from: location }} replace />;
  if (permission && !hasPermission(permission)) return <Navigate to="/403" replace />;
  return <Outlet />;
}

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    element: <RequireAuth />,
    children: [{
      element: <MainLayout />,
      children: [
        { index: true, element: <DashboardPage /> },
        { element: <RequireAuth permission="project.read" />, children: [
          { path: 'projects', element: <ProjectListPage /> },
          { path: 'projects/:id', element: <ProjectDetailPage /> },
          { path: 'projects/:id/board', element: <TaskBoardPage /> },
        ]},
        { path: 'tasks/my', element: <MyTasksPage /> },
        { element: <RequireAuth permission="sop.read" />, children: [
          { path: 'sops', element: <SopListPage /> },
          { path: 'sops/:id', element: <SopViewPage /> },
        ]},
        { element: <RequireAuth permission="sop.edit" />, children: [
          { path: 'sops/new', element: <SopEditorPage /> },
          { path: 'sops/:id/edit', element: <SopEditorPage /> },
        ]},
        { element: <RequireAuth permission="sop.approve" />, children: [
          { path: 'sops/approvals', element: <SopApprovalPage /> },
        ]},
        { element: <RequireAuth permission="user.manage" />, children: [
          { path: 'admin/users', element: <UserListPage /> },
          { path: 'admin/roles', element: <RoleManagementPage /> },
        ]},
      ],
    }],
  },
]);
```

### E.3 State Management

```ts
// src/features/auth/store/authStore.ts  (Zustand)
interface AuthState {
  accessToken: string | null;      // disimpan di memori, BUKAN localStorage
  user: { id: string; name: string; tenantId: string } | null;
  permissions: string[];
  isAuthenticated: boolean;
  setSession: (token: string, user: AuthState['user'], permissions: string[]) => void;
  clear: () => void;
  hasPermission: (p: string) => boolean;
}

export const useAuthStore = create<AuthState>()((set, get) => ({
  accessToken: null, user: null, permissions: [], isAuthenticated: false,
  setSession: (accessToken, user, permissions) =>
    set({ accessToken, user, permissions, isAuthenticated: true }),
  clear: () => set({ accessToken: null, user: null, permissions: [], isAuthenticated: false }),
  hasPermission: (p) => get().permissions.includes(p),
}));
```

Access token disimpan di memori; refresh token di **httpOnly secure cookie** yang dikirim otomatis ke `/api/auth/refresh`. Saat halaman di-refresh, aplikasi memanggil endpoint refresh untuk memulihkan sesi.

### E.4 Desain Halaman

| Halaman | Isi utama | Komponen kunci |
| --- | --- | --- |
| **Dashboard** | Kartu ringkasan (proyek aktif, tugas jatuh tempo minggu ini, tugas terlambat, SOP menunggu approval), grafik tugas per status, daftar "Tugas saya", SOP baru terbit yang belum dibaca | `StatCard`, `TaskStatusChart`, `MyTasksWidget`, `PendingSopWidget` |
| **Manajemen Proyek** | Tabel proyek (filter status, pemilik, departemen, pencarian), tombol buat proyek; detail proyek dengan tab Ringkasan, Board, Anggota, Milestone, SOP terkait, Aktivitas | `DataTable`, `ProjectForm` (drawer), `MemberPicker`, `MilestoneTimeline` |
| **Manajemen Tugas** | Kanban drag-and-drop per status + tampilan list; drawer detail tugas: deskripsi, assignee, due date, checklist (termasuk dari SOP), komentar, lampiran, riwayat | `KanbanBoard` (dnd-kit), `TaskDrawer`, `ChecklistPanel`, `CommentThread` |
| **Manajemen SOP** | Daftar SOP per kategori + badge status; halaman baca dengan daftar isi & tombol "Saya sudah membaca"; editor rich text + panel langkah; riwayat versi & diff; antrian approval | `SopEditor` (TipTap), `StepListEditor`, `VersionHistory`, `DiffViewer`, `ApprovalPanel` |
| **User Management** | Tabel user (status, departemen, role), undang user, ubah role; halaman role dengan matriks permission (baris = permission, kolom = role, checkbox) | `UserTable`, `InviteUserModal`, `PermissionMatrix` |

Deskripsi tata letak umum (wireframe teks):

```
┌───────────────────────────────────────────────────────────────────┐
│ Logo  │ Cari...                         🔔(3)   Nama User ▾       │  ← Header
├───────┼───────────────────────────────────────────────────────────┤
│ Dash  │  Dashboard                                                │
│ Proj  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐          │
│ Tugas │  │Proyek   │ │Due minggu│ │Terlambat│ │SOP butuh│          │
│ SOP   │  │aktif 12 │ │ini   18 │ │      4  │ │approval 2│         │
│ Admin │  └─────────┘ └─────────┘ └─────────┘ └─────────┘          │
│       │  ┌──────── Tugas per status ──────┐ ┌── Tugas saya ─────┐ │
│       │  │  ▇▇▇ ▇▇ ▇▇▇▇ ▇                  │ │ □ Review kontrak  │ │
│       │  └────────────────────────────────┘ │ □ Update SOP HR   │ │
│       │                                      └───────────────────┘ │
└───────┴───────────────────────────────────────────────────────────┘
  Sidebar (menu tampil sesuai permission)
```

### E.5 Contoh Pemanggilan API

```ts
// src/shared/api/httpClient.ts
import axios from 'axios';
import { useAuthStore } from '@/features/auth/store/authStore';

export const http = axios.create({ baseURL: import.meta.env.VITE_API_BASE_URL, withCredentials: true });

http.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  config.headers['X-Correlation-Id'] = crypto.randomUUID();
  return config;
});

let refreshing: Promise<string> | null = null;
http.interceptors.response.use(undefined, async (error) => {
  const original = error.config;
  if (error.response?.status === 401 && !original._retry) {
    original._retry = true;
    refreshing ??= http.post('/auth/refresh').then((r) => {
      useAuthStore.getState().setSession(r.data.accessToken, r.data.user, r.data.permissions);
      return r.data.accessToken as string;
    }).finally(() => { refreshing = null; });
    try {
      const token = await refreshing;
      original.headers.Authorization = `Bearer ${token}`;
      return http(original);
    } catch {
      useAuthStore.getState().clear();
      window.location.assign('/login');
    }
  }
  return Promise.reject(error);
});
```

```ts
// src/features/tasks/api/taskApi.ts
export interface TaskDto { id: string; title: string; status: TaskStatus; priority: Priority; dueDate?: string; }
export interface CreateTaskInput { projectId: string; title: string; description?: string;
  priority: Priority; dueDate?: string; assigneeIds: string[]; }

export const taskApi = {
  list: (p: { projectId: string; status?: string; page?: number; size?: number }) =>
    http.get<PagedResult<TaskDto>>('/tasks', { params: p }).then((r) => r.data),
  get: (id: string) => http.get<TaskDto>(`/tasks/${id}`).then((r) => r.data),
  create: (body: CreateTaskInput) => http.post<{ id: string }>('/tasks', body).then((r) => r.data),
  changeStatus: (id: string, status: TaskStatus) => http.patch(`/tasks/${id}/status`, { status }),
};

// src/features/tasks/hooks/useTasks.ts  (TanStack Query)
export const taskKeys = {
  list: (projectId: string, status?: string) => ['tasks', projectId, status] as const,
};

export function useProjectTasks(projectId: string, status?: string) {
  return useQuery({
    queryKey: taskKeys.list(projectId, status),
    queryFn: () => taskApi.list({ projectId, status, page: 1, size: 100 }),
    staleTime: 30_000,
  });
}

export function useChangeTaskStatus(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: TaskStatus }) => taskApi.changeStatus(id, status),
    onMutate: async ({ id, status }) => {              // optimistic update untuk kanban
      await qc.cancelQueries({ queryKey: ['tasks', projectId] });
      const prev = qc.getQueryData(taskKeys.list(projectId));
      qc.setQueryData(taskKeys.list(projectId), (old: PagedResult<TaskDto> | undefined) =>
        old && { ...old, items: old.items.map((t) => (t.id === id ? { ...t, status } : t)) });
      return { prev };
    },
    onError: (_e, _v, ctx) => qc.setQueryData(taskKeys.list(projectId), ctx?.prev),
    onSettled: () => qc.invalidateQueries({ queryKey: ['tasks', projectId] }),
  });
}

// Pemakaian di komponen
export function CreateTaskForm({ projectId }: { projectId: string }) {
  const qc = useQueryClient();
  const { register, handleSubmit, formState: { errors } } =
    useForm<CreateTaskInput>({ resolver: zodResolver(createTaskSchema), defaultValues: { projectId, priority: 'MEDIUM', assigneeIds: [] } });
  const create = useMutation({
    mutationFn: taskApi.create,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tasks', projectId] }),
  });

  return (
    <form onSubmit={handleSubmit((v) => create.mutate(v))}>
      <input {...register('title')} placeholder="Judul tugas" />
      {errors.title && <span role="alert">{errors.title.message}</span>}
      <button type="submit" disabled={create.isPending}>Simpan</button>
    </form>
  );
}
```

Komponen `<Can permission="sop.approve">…</Can>` dipakai untuk menyembunyikan tombol yang tidak boleh diakses; ingat ini hanya kenyamanan UI, pemeriksaan sebenarnya tetap di backend.

## F. Perencanaan DevOps

Setiap service dibangun menjadi image Docker sendiri, diuji dan dipindai di pipeline CI, lalu di-deploy ke Kubernetes dengan Helm melalui tiga lingkungan: **dev → staging → production** (production butuh approval manual).

### F.1 Opsi Deployment

| Opsi | Cocok untuk | Kelebihan | Kekurangan |
| --- | --- | --- | --- |
| Docker Compose | Dev lokal, demo | Sederhana, satu perintah `docker compose up` | Tidak ada auto-scaling/self-healing |
| Kubernetes (on-prem/OpenShift) | Enterprise yang wajib data on-premise (umum di perbankan/BUMN) | Kontrol penuh, sesuai regulasi data | Butuh tim platform |
| Kubernetes terkelola (AKS/EKS/GKE) | Organisasi yang boleh cloud | Operasional lebih ringan, integrasi monitoring cloud | Biaya cloud, pertimbangan residensi data |
| GitOps (Argo CD) di atas Kubernetes | Banyak service & lingkungan | Deploy deklaratif, audit lewat Git, rollback mudah | Kurva belajar tambahan |

Rekomendasi: Docker Compose untuk lokal, Kubernetes + Helm untuk staging/production, dan Argo CD bila jumlah service bertambah.

### F.2 Pipeline CI/CD

Tahapan pipeline (berlaku sama untuk GitHub Actions, GitLab CI, atau Azure DevOps):

1. **Trigger:** pull request (build + test) dan merge ke `main` (build + deploy); hanya service yang berubah yang dibangun (path filter).
2. **Build & unit test:** `dotnet restore/build/test`, `npm ci && npm run lint && npm test`.
3. **Analisis kualitas & keamanan:** SonarQube (code smell, coverage ≥ 70%), SAST (CodeQL/Semgrep), dependency scan (`dotnet list package --vulnerable`, `npm audit`).
4. **Integration test:** Testcontainers (Oracle Free + Redis) di pipeline.
5. **Build image:** Docker multi-stage, tag = `git sha` + versi semver.
6. **Scan image:** Trivy; gagal jika ada CVE severity HIGH/CRITICAL yang bisa diperbaiki.
7. **Push** ke registry (GHCR/Harbor/ACR).
8. **Migrasi DB:** Flyway/Liquibase sebagai Kubernetes Job (dev & staging otomatis, production setelah approval).
9. **Deploy:** `helm upgrade --install` ke dev otomatis → staging otomatis + smoke test → production dengan manual approval, strategi rolling update atau canary.
10. **Post-deploy:** smoke test, cek health, notifikasi ke Teams/Slack; rollback otomatis bila health check gagal.

Contoh GitHub Actions untuk Task Service:

```yaml
# .github/workflows/task-service.yml
name: task-service
on:
  push:
    branches: [main]
    paths: ['src/Services/Task/**', 'src/BuildingBlocks/**']
  pull_request:
    paths: ['src/Services/Task/**', 'src/BuildingBlocks/**']

env:
  IMAGE: ghcr.io/${{ github.repository_owner }}/task-service

jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore src/Services/Task/Task.Api
      - run: dotnet build src/Services/Task/Task.Api -c Release --no-restore
      - run: dotnet test src/Services/Task/tests -c Release --collect:"XPlat Code Coverage"

  image:
    needs: build-test
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    permissions: { contents: read, packages: write }
    steps:
      - uses: actions/checkout@v4
      - uses: docker/login-action@v3
        with: { registry: ghcr.io, username: ${{ github.actor }}, password: ${{ secrets.GITHUB_TOKEN }} }
      - uses: docker/build-push-action@v6
        with:
          context: .
          file: src/Services/Task/Task.Api/Dockerfile
          push: true
          tags: ${{ env.IMAGE }}:${{ github.sha }}
      - uses: aquasecurity/trivy-action@master
        with: { image-ref: '${{ env.IMAGE }}:${{ github.sha }}', severity: 'CRITICAL,HIGH', exit-code: '1', ignore-unfixed: true }

  deploy-staging:
    needs: image
    runs-on: ubuntu-latest
    environment: staging
    steps:
      - uses: actions/checkout@v4
      - uses: azure/setup-helm@v4
      - run: echo "${{ secrets.KUBECONFIG_STAGING }}" > kubeconfig
      - run: >
          helm upgrade --install task-service deploy/helm/service
          -f deploy/helm/values/task-service.staging.yaml
          --set image.tag=${{ github.sha }} --namespace app --kubeconfig kubeconfig --wait --atomic

  deploy-production:
    needs: deploy-staging
    runs-on: ubuntu-latest
    environment: production          # dikonfigurasi "required reviewers" = approval manual
    steps:
      - uses: actions/checkout@v4
      - uses: azure/setup-helm@v4
      - run: echo "${{ secrets.KUBECONFIG_PROD }}" > kubeconfig
      - run: >
          helm upgrade --install task-service deploy/helm/service
          -f deploy/helm/values/task-service.prod.yaml
          --set image.tag=${{ github.sha }} --namespace app --kubeconfig kubeconfig --wait --atomic
```

Padanan di platform lain: GitLab CI memakai `stages: [build, test, scan, image, deploy]` dengan `rules: changes:` untuk path filter dan `when: manual` untuk production; Azure DevOps memakai multi-stage YAML pipeline dengan **Environments + Approvals and checks**.

### F.3 Dockerfile Backend

```dockerfile
# src/Services/Task/Task.Api/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Task/Task.Api/Task.Api.csproj", "src/Services/Task/Task.Api/"]
COPY ["src/Services/Task/Task.Application/Task.Application.csproj", "src/Services/Task/Task.Application/"]
COPY ["src/Services/Task/Task.Domain/Task.Domain.csproj", "src/Services/Task/Task.Domain/"]
COPY ["src/Services/Task/Task.Infrastructure/Task.Infrastructure.csproj", "src/Services/Task/Task.Infrastructure/"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]
RUN dotnet restore "src/Services/Task/Task.Api/Task.Api.csproj"
COPY . .
RUN dotnet publish "src/Services/Task/Task.Api/Task.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
# user non-root bawaan image .NET 10
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Task.Api.dll"]
```

### F.4 Dockerfile Frontend

```dockerfile
# web/Dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
```

```nginx
# web/nginx.conf
server {
  listen 8080;
  root /usr/share/nginx/html;
  location / { try_files $uri /index.html; }                 # SPA fallback
  location ~* \.(js|css|woff2|png|svg)$ { expires 1y; add_header Cache-Control "public, immutable"; }
  add_header X-Content-Type-Options nosniff;
  add_header X-Frame-Options DENY;
  add_header Content-Security-Policy "default-src 'self'; connect-src 'self' wss:; img-src 'self' data:;";
}
```

### F.5 Deployment ke Kubernetes

```yaml
# deploy/helm/service/templates/deployment.yaml (hasil render untuk task-service)
apiVersion: apps/v1
kind: Deployment
metadata: { name: task-service, namespace: app, labels: { app: task-service } }
spec:
  replicas: 3
  strategy: { type: RollingUpdate, rollingUpdate: { maxUnavailable: 0, maxSurge: 1 } }
  selector: { matchLabels: { app: task-service } }
  template:
    metadata: { labels: { app: task-service } }
    spec:
      securityContext: { runAsNonRoot: true }
      containers:
        - name: api
          image: ghcr.io/company/task-service:<sha>
          ports: [{ containerPort: 8080 }]
          env:
            - { name: ASPNETCORE_ENVIRONMENT, value: Production }
            - name: ConnectionStrings__Oracle
              valueFrom: { secretKeyRef: { name: task-service-secrets, key: oracle } }
            - name: ConnectionStrings__Redis
              valueFrom: { secretKeyRef: { name: task-service-secrets, key: redis } }
          resources:
            requests: { cpu: 200m, memory: 256Mi }
            limits:   { cpu: '1',  memory: 512Mi }
          readinessProbe: { httpGet: { path: /health/ready, port: 8080 }, periodSeconds: 10 }
          livenessProbe:  { httpGet: { path: /health/live,  port: 8080 }, periodSeconds: 20 }
          securityContext: { readOnlyRootFilesystem: true, allowPrivilegeEscalation: false }
---
apiVersion: v1
kind: Service
metadata: { name: task-service, namespace: app }
spec: { selector: { app: task-service }, ports: [{ port: 80, targetPort: 8080 }] }
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata: { name: task-service, namespace: app }
spec:
  scaleTargetRef: { apiVersion: apps/v1, kind: Deployment, name: task-service }
  minReplicas: 3
  maxReplicas: 10
  metrics: [{ type: Resource, resource: { name: cpu, target: { type: Utilization, averageUtilization: 70 } } }]
---
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata: { name: task-service, namespace: app }
spec: { minAvailable: 2, selector: { matchLabels: { app: task-service } } }
```

Komponen cluster lainnya:

| Komponen | Pilihan |
| --- | --- |
| Ingress + TLS | NGINX Ingress Controller + cert-manager |
| Secrets | External Secrets Operator + HashiCorp Vault / Azure Key Vault |
| Network | NetworkPolicy: hanya Gateway yang boleh memanggil service; service hanya boleh ke Oracle/Redis/broker |
| Redis | Bitnami Redis (Sentinel) atau Redis terkelola |
| Broker | RabbitMQ Cluster Operator atau Strimzi (Kafka) |
| Oracle | Di luar cluster (VM/Exadata dikelola DBA) — umumnya lebih aman untuk database produksi |

### F.6 Logging & Monitoring

| Pilar | Alat | Implementasi |
| --- | --- | --- |
| Log | Serilog → **ELK** (Filebeat/Fluent Bit → Elasticsearch → Kibana) atau **Loki** | JSON terstruktur, field wajib: `timestamp, level, service, tenantId, userId, correlationId, traceId` |
| Metrik | OpenTelemetry → **Prometheus** → **Grafana** | Request rate, error rate, latency p95/p99 (RED), pool koneksi Oracle, hit ratio Redis, panjang antrean broker |
| Trace | OpenTelemetry → **Tempo** atau **Jaeger** | Melacak satu request melewati Gateway → service → Oracle/Redis/broker |
| Alert | Alertmanager / Grafana Alerting → Teams/Slack/email | Contoh: error 5xx > 2% selama 5 menit, latency p95 > 800 ms, pod restart berulang, Redis down |
| Uptime | Blackbox exporter / Uptime Kuma | Cek endpoint publik tiap menit |

```csharp
// Company.BuildingBlocks.Observability — dipanggil di Program.cs setiap service
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "task-service")
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("task-service"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddEntityFrameworkCoreInstrumentation()
                       .AddRedisInstrumentation()
                       .AddOtlpExporter())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation()
                       .AddRuntimeInstrumentation()
                       .AddPrometheusExporter());

app.MapPrometheusScrapingEndpoint();   // /metrics
```

Rekomendasi: pilih satu stack agar tim tidak terpecah — **Grafana stack (Loki + Tempo + Prometheus)** lebih ringan dan murah; **ELK** lebih kuat untuk pencarian log kompleks dan sering sudah tersedia di perusahaan besar.

## G. Perencanaan Keamanan

Keamanan diterapkan berlapis: Gateway menolak request tanpa token valid atau yang melebihi batas, setiap service memvalidasi ulang JWT dan permission, data difilter per tenant di aplikasi dan di Oracle (VPD), dan setiap perubahan penting tercatat di audit log. Acuan yang disarankan: **OWASP ASVS Level 2** dan **OWASP API Security Top 10**.

### G.1 JWT Authentication

| Parameter | Nilai rekomendasi |
| --- | --- |
| Algoritma | RS256/ES256 (kunci privat hanya di Auth Service; service lain mengambil public key dari JWKS) |
| Umur access token | 15 menit |
| Umur refresh token | 7 hari, rotasi setiap dipakai, deteksi reuse → revoke seluruh sesi |
| Klaim | `sub`, `tid` (tenant), `jti`, `iss`, `aud`, `exp`, `roles`; permission detail diambil dari cache, bukan semua dimasukkan ke token |
| Penyimpanan di browser | Access token di memori; refresh token di cookie `HttpOnly; Secure; SameSite=Strict` |
| Logout | Revoke refresh token + simpan `jti` di Redis blacklist sampai token kedaluwarsa |

```csharp
// Company.BuildingBlocks.Auth — dipakai Gateway dan semua service
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];   // https://auth.company.local
        o.Audience  = "platform-api";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var jti = ctx.Principal!.FindFirstValue(JwtRegisteredClaimNames.Jti);
                var redis = ctx.HttpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>();
                if (await redis.GetDatabase().KeyExistsAsync($"auth:revoked:{jti}"))
                    ctx.Fail("Token sudah dicabut.");
            }
        };
    });
```

### G.2 Role-Based Access Control (RBAC)

Model: **User → Role → Permission**, dengan *scope* (global, departemen, atau proyek). Permission berbentuk `modul.aksi` dan diperiksa di backend; frontend hanya menyembunyikan tombol.

| Role (default) | Contoh permission |
| --- | --- |
| `SUPER_ADMIN` (platform) | kelola tenant |
| `TENANT_ADMIN` | `user.manage`, `role.manage`, `sop.category.manage`, semua permission baca |
| `PROJECT_MANAGER` | `project.create`, `project.update`, `project.member.manage`, `task.*` dalam proyeknya |
| `MEMBER` | `project.read`, `task.read`, `task.create`, `task.update` (tugas sendiri), `sop.read` |
| `SOP_AUTHOR` | `sop.create`, `sop.edit`, `sop.submit` |
| `SOP_REVIEWER` | `sop.review` |
| `SOP_APPROVER` | `sop.approve`, `sop.publish` |
| `AUDITOR` | `audit.read`, semua permission baca (read-only) |

```csharp
// Policy dinamis: [Authorize(Policy = "sop.approve")]
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler(IPermissionService perms) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        var userId = ctx.User.FindFirstValue("sub");
        var tenantId = ctx.User.FindFirstValue("tid");
        if (userId is null || tenantId is null) return;
        // diambil dari Redis tnt:{t}:perm:{userId}, fallback ke User Service
        var granted = await perms.GetPermissionsAsync(long.Parse(tenantId), long.Parse(userId));
        if (granted.Contains(req.Permission)) ctx.Succeed(req);
    }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> o)
    : DefaultAuthorizationPolicyProvider(o)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string name) =>
        await base.GetPolicyAsync(name) ??
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(name)).Build();
}
```

Selain permission, terapkan **pemeriksaan kepemilikan objek** (mis. user hanya boleh mengubah tugas di proyek tempat ia menjadi anggota) untuk mencegah *Broken Object Level Authorization*, kerentanan API nomor satu menurut OWASP.

### G.3 Input Validation

- Validasi di **dua sisi**: Zod di React (pengalaman pengguna) dan **FluentValidation** di Application layer (wajib, sumber kebenaran).
- Semua query Oracle memakai **bind variable**; tidak ada string concatenation SQL.
- Konten rich text SOP disanitasi di server dengan **HtmlSanitizer** (Ganss.Xss) memakai allowlist tag; render di React tanpa `dangerouslySetInnerHTML` kecuali konten yang sudah disanitasi.
- Upload file: batasi ukuran (mis. 20 MB) dan tipe (cek magic bytes, bukan hanya ekstensi), pindai antivirus (ClamAV), simpan di object storage dengan nama acak, unduh via URL bertanda tangan berumur pendek.
- Batasi ukuran body request di Kestrel, tolak properti JSON yang tidak dikenal untuk mencegah *mass assignment*, kembalikan error dalam ProblemDetails tanpa stack trace.

### G.4 Rate Limiting di API Gateway

| Policy | Kunci | Batas | Algoritma |
| --- | --- | --- | --- |
| `login` | IP + username | 5 percobaan / 15 menit | Fixed window + lockout di Auth |
| `per-user` | `sub` dari JWT | 300 request / menit | Sliding window |
| `per-tenant` | `tid` | 5.000 request / menit | Token bucket |
| `upload` | `sub` | 20 / menit | Fixed window |

```csharp
// Gateway.Api/Program.cs
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy("per-user", ctx => RateLimitPartition.GetSlidingWindowLimiter(
        ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 300, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0
        }));

    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15) }));
});

app.UseRateLimiter();
app.MapReverseProxy();
```

Rate limiter bawaan ASP.NET Core menyimpan hitungan di memori per pod. Dengan beberapa replika Gateway, gunakan limiter berbasis **Redis** (mis. paket `RedisRateLimiting`) agar batas berlaku global, atau pindahkan rate limiting ke Kong/NGINX Ingress.

### G.5 Audit Log

- **Apa yang dicatat:** login/logout/gagal login, perubahan role & permission, CRUD proyek/tugas, seluruh siklus SOP (buat, ubah, submit, approve/reject, publish, acknowledge), ekspor data, akses ditolak (403).
- **Isi:** siapa (user, tenant, IP, user agent), kapan (UTC), apa (action, entity, id), nilai sebelum/sesudah (JSON, field sensitif di-mask), `correlationId`.
- **Cara:** setiap service mem-publish event audit lewat outbox → **Audit Service** menyimpan ke `AUD_SCHEMA.AUDIT_LOGS` (partisi bulanan). Interceptor EF Core (`SaveChangesInterceptor`) menangkap perubahan entitas secara otomatis.
- **Integritas:** tabel audit *append-only* (user aplikasi hanya punya hak `INSERT` dan `SELECT`); opsional hash berantai per baris atau **Oracle Blockchain/Immutable Table** (21c+) untuk bukti tidak diubah.
- **Retensi:** online 1–2 tahun, arsip sesuai kebijakan perusahaan/regulator (sering 5–10 tahun di sektor keuangan).

### G.6 Kontrol Tambahan

| Area | Kontrol |
| --- | --- |
| Transport | TLS 1.2+ di ingress; mTLS antar service via service mesh (Istio/Linkerd) bila diwajibkan |
| Data at rest | Oracle TDE (Transparent Data Encryption); Redis dengan password + TLS |
| Secrets | Vault/Key Vault, tidak ada secret di repo atau image |
| Header | HSTS, CSP, X-Content-Type-Options, X-Frame-Options |
| CORS | Hanya domain frontend resmi |
| Dependensi | Dependabot/Renovate + scan di CI |
| Pengujian | Pentest berkala, DAST (OWASP ZAP) di staging |

## H. Perencanaan SOP Builder

SOP Builder memungkinkan tiap perusahaan menulis SOP sendiri dengan editor rich text, menyusunnya sebagai langkah-langkah terstruktur, melewati review dan approval berjenjang, lalu menerbitkannya sebagai versi resmi yang bisa ditautkan ke proyek dan diubah menjadi checklist tugas. Versi lama tidak pernah dihapus atau ditimpa.

### H.1 Fitur

| Fitur | Deskripsi |
| --- | --- |
| Kategori & kode SOP | Hierarki kategori per tenant (mis. HR, Keuangan, Operasional); kode otomatis `SOP-{KAT}-{NNN}` |
| Template | Template bawaan (Tujuan, Ruang Lingkup, Definisi, Tanggung Jawab, Prosedur, Dokumen Terkait, Lampiran) dan template buatan tenant |
| Editor rich text | Heading, list, tabel, gambar, tautan, callout "Perhatian/Catatan", mention user/role |
| Langkah terstruktur | Daftar langkah dengan nomor, penanggung jawab (role), wajib/opsional; dipakai untuk checklist tugas |
| Diagram alir (opsional) | Blok flowchart sederhana di dalam SOP |
| Metadata | Pemilik (departemen), tanggal efektif, tanggal review berikutnya, dokumen referensi, tag |
| Autosave & lock | Simpan otomatis tiap 10 detik (Redis), lock edit agar tidak ada dua penulis menimpa |
| Komentar review | Reviewer memberi komentar pada bagian teks tertentu |
| Approval berjenjang | Alur approval per kategori (mis. Kepala Divisi → Compliance → Direktur) |
| Versioning & diff | Riwayat versi, perbandingan dua versi, catatan perubahan wajib |
| Publikasi & distribusi | Notifikasi ke role/departemen terkait, SOP tampil di portal baca |
| Acknowledgement | User wajib klik "Saya sudah membaca dan memahami"; laporan siapa yang belum |
| Review berkala | Pengingat otomatis saat mendekati `REVIEW_DUE_DATE` |
| Ekspor | PDF bertanda versi & tanggal efektif (QuestPDF di server) |
| Integrasi tugas | SOP ditautkan ke proyek; langkah SOP menjadi checklist pada tugas |

### H.2 Penyimpanan di Oracle

- `SOP_DOCUMENTS` menyimpan identitas SOP yang stabil (kode, judul, kategori, pemilik) dan pointer `CURRENT_VERSION_ID` ke versi yang sedang berlaku.
- `SOP_VERSIONS` menyimpan isi setiap versi: **`CONTENT_JSON`** (dokumen asli editor TipTap/ProseMirror, untuk diedit ulang tanpa kehilangan struktur) dan **`CONTENT_HTML`** (hasil render yang sudah disanitasi, untuk tampilan cepat, pencarian Oracle Text, dan ekspor PDF).
- `SOP_STEPS` menyimpan langkah per versi, sehingga checklist tugas selalu menunjuk ke versi tertentu.
- Gambar/lampiran disimpan di object storage (MinIO/S3/Azure Blob); Oracle hanya menyimpan metadata dan kuncinya.
- Versi yang sudah `PUBLISHED` bersifat **immutable**: perubahan selalu membuat versi baru.

```sql
-- Membuat versi baru dari versi yang sedang berlaku (dijalankan dalam satu transaksi)
INSERT INTO SOP_VERSIONS (SOP_ID, VERSION_MAJOR, VERSION_MINOR, STATUS, CONTENT_JSON, CONTENT_HTML, CREATED_BY)
SELECT v.SOP_ID, v.VERSION_MAJOR, v.VERSION_MINOR + 1, 'DRAFT', v.CONTENT_JSON, v.CONTENT_HTML, :userId
  FROM SOP_VERSIONS v
  JOIN SOP_DOCUMENTS d ON d.CURRENT_VERSION_ID = v.ID
 WHERE d.ID = :sopId AND d.TENANT_ID = :tenantId;

-- Publikasi: versi lama → SUPERSEDED, versi baru → PUBLISHED, pointer dipindah
UPDATE SOP_VERSIONS SET STATUS = 'SUPERSEDED'
 WHERE SOP_ID = :sopId AND STATUS = 'PUBLISHED';

UPDATE SOP_VERSIONS
   SET STATUS = 'PUBLISHED', PUBLISHED_AT = SYSTIMESTAMP, PUBLISHED_BY = :userId
 WHERE ID = :versionId AND STATUS = 'APPROVED';

UPDATE SOP_DOCUMENTS SET CURRENT_VERSION_ID = :versionId, UPDATED_AT = SYSTIMESTAMP
 WHERE ID = :sopId;
```

### H.3 Editor SOP (Rich Text)

Rekomendasi **TipTap** (berbasis ProseMirror): open source, output JSON terstruktur, mudah ditambah blok kustom (langkah SOP, callout), dan mendukung kolaborasi real-time via Yjs bila nanti dibutuhkan. Alternatif: Lexical (Meta) atau CKEditor 5 (lisensi komersial untuk fitur lanjutan).

```tsx
// src/features/sops/components/SopEditor.tsx
import { useEditor, EditorContent } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import Table from '@tiptap/extension-table';
import TableRow from '@tiptap/extension-table-row';
import TableCell from '@tiptap/extension-table-cell';
import TableHeader from '@tiptap/extension-table-header';
import Image from '@tiptap/extension-image';
import Link from '@tiptap/extension-link';
import Placeholder from '@tiptap/extension-placeholder';
import { useDebouncedCallback } from 'use-debounce';

interface Props { sopId: string; versionId: string; initialContent: JSONContent; readOnly?: boolean; }

export function SopEditor({ sopId, versionId, initialContent, readOnly }: Props) {
  const saveDraft = useSaveSopDraft(sopId, versionId);              // PUT /api/sops/{id}/versions/{versionId}
  const autosave = useDebouncedCallback((json: JSONContent, html: string) =>
    saveDraft.mutate({ contentJson: json, contentHtml: html }), 10_000);

  const editor = useEditor({
    editable: !readOnly,
    content: initialContent,
    extensions: [
      StarterKit,
      Table.configure({ resizable: true }), TableRow, TableHeader, TableCell,
      Image, Link.configure({ openOnClick: false }),
      Placeholder.configure({ placeholder: 'Tulis prosedur di sini…' }),
      SopStepBlock,                                                 // node kustom untuk langkah SOP
    ],
    onUpdate: ({ editor }) => autosave(editor.getJSON(), editor.getHTML()),
  });

  return (
    <div className="sop-editor">
      {!readOnly && <EditorToolbar editor={editor} />}
      <EditorContent editor={editor} />
      <SaveIndicator state={saveDraft.status} />
    </div>
  );
}
```

Saat halaman editor dibuka, frontend memanggil `POST /api/sops/{id}/lock` (Redis `SET lock:sop:{id}:edit {userId} NX EX 300`, diperpanjang tiap 2 menit). Bila lock dipegang orang lain, editor terbuka dalam mode baca dengan info "Sedang diedit oleh …".

### H.4 Versioning SOP

| Aturan | Penjelasan |
| --- | --- |
| Penomoran | `MAJOR.MINOR`: perubahan substansi prosedur = major (2.0); perbaikan redaksi = minor (1.1). Penulis memilih jenis saat membuat versi baru |
| Satu draft aktif | Hanya satu versi `DRAFT`/`IN_REVIEW` per SOP pada satu waktu |
| Immutable | Versi `PUBLISHED` tidak bisa diedit; perubahan = versi baru |
| Satu yang berlaku | Hanya satu versi `PUBLISHED` (dijaga unique index); versi sebelumnya otomatis `SUPERSEDED` |
| Catatan perubahan | `CHANGE_SUMMARY` wajib diisi sebelum submit |
| Diff | Dihitung di frontend dari dua `CONTENT_JSON`/HTML (mis. `diff` + render highlight), atau di server untuk ekspor |
| Acknowledgement ulang | Versi major baru mewajibkan user terkait membaca ulang; versi minor tidak |
| Pensiun | SOP yang tidak dipakai di-`ARCHIVED`, tetap bisa dilihat di riwayat |

Siklus status versi:

```
            submit              semua level approve          publish
 DRAFT ───────────────► IN_REVIEW ───────────────► APPROVED ───────────► PUBLISHED
   ▲                        │                                               │
   │       reject           │                         versi baru publish    ▼
   └──── (kembali ke draft) ┘                                          SUPERSEDED

 PUBLISHED / SUPERSEDED ──(SOP dinonaktifkan)──► ARCHIVED
```

### H.5 Role Pembuat dan Penyetuju SOP

| Role | Membuat/edit draft | Submit | Review & komentar | Approve/reject | Publish | Kelola alur & kategori |
| --- | --- | --- | --- | --- | --- | --- |
| `SOP_AUTHOR` | Ya | Ya | — | — | — | — |
| `SOP_REVIEWER` | — | — | Ya | Rekomendasi | — | — |
| `SOP_APPROVER` | — | — | Ya | Ya (sesuai level) | Ya | — |
| `TENANT_ADMIN` | — | — | — | — | — | Ya |
| `MEMBER` | — | — | — | — | — | — (hanya baca & acknowledge) |

Aturan tambahan:

1. **Segregation of duties:** penulis tidak boleh menyetujui SOP yang ia tulis sendiri (dicek di SOP Service, bukan hanya di UI).
2. Alur approval dikonfigurasi per kategori di `SOP_APPROVAL_FLOWS` (level 1, 2, 3…); level berikutnya baru aktif setelah level sebelumnya setuju; satu penolakan mengembalikan versi ke `DRAFT` dengan catatan.
3. Setiap keputusan (approve/reject + catatan) tersimpan di `SOP_APPROVALS` dan audit log; opsional tanda tangan elektronik (re-autentikasi/OTP saat approve).
4. SLA approval (mis. 3 hari kerja) dengan pengingat dan eskalasi otomatis lewat Notification Service.

```csharp
// SOP Service — handler approve (ringkas)
public async Task Handle(ApproveSopVersionCommand cmd, CancellationToken ct)
{
    var version = await repo.GetVersionAsync(cmd.VersionId, ct) ?? throw new NotFoundException();
    if (version.Status != SopVersionStatus.InReview)
        throw new DomainException("Versi tidak sedang dalam review.");
    if (version.CreatedBy == user.Id)
        throw new ForbiddenException("Penulis tidak boleh menyetujui SOP-nya sendiri.");

    var level = await flows.GetCurrentLevelAsync(version, ct);
    if (!await flows.IsApproverAsync(level, user.Id, ct))
        throw new ForbiddenException("Anda bukan approver untuk level ini.");

    version.RecordApproval(level.LevelNo, user.Id, cmd.Note);   // → APPROVED jika level terakhir
    await uow.SaveChangesAsync(ct);                             // + outbox SopApproved
    await cache.RemoveAsync($"tnt:{user.TenantId}:sop:{version.SopId}:published", ct);
}
```

## Roadmap Implementasi dan Best Practice

Bangun fondasi platform (pipeline, gateway, auth) lebih dulu, lalu layanan inti, baru SOP Builder; setiap fase baru dimulai setelah gate-nya terpenuhi, sehingga risiko teknis terbesar terselesaikan paling awal.

Fase 1 Fondasi → gate: pipeline jalan ke dev · Fase 2 Layanan inti → gate: alur tugas lulus UAT · Fase 3 SOP Builder → gate: alur SOP lulus uji · Fase 4 Go-live → gate: siap produksi. Detail sprint ada di `SCRUM.md`.

Durasi tiap fase bergantung pada ukuran tim; dengan 4–6 developer, fase 1–2 biasanya menjadi bagian terpanjang karena semua pola bersama (auth, outbox, cache, logging) dibangun di sana.

### Rangkuman Best Practice

| Area | Lakukan | Hindari |
| --- | --- | --- |
| Batas service | Pisah per bounded context; mulai dari *modular monolith* bila tim kecil, pecah saat perlu | Service terlalu kecil (nano-service) yang saling memanggil berantai |
| Data | Satu schema Oracle per service, outbox untuk event, read model lokal | Join lintas schema, transaksi terdistribusi (2PC) |
| Komunikasi | Event untuk notifikasi, REST/gRPC dengan timeout + circuit breaker untuk query | Rantai panggilan sinkron panjang |
| Cache | Cache-aside, TTL pendek, invalidasi saat tulis, prefix tenant | Menjadikan Redis sumber kebenaran; cache tanpa TTL |
| Oracle | Bind variable, index sesuai pola query, partisi tabel tumbuh cepat, Flyway/Liquibase | SQL dirangkai string, `SELECT *`, migrasi manual |
| Keamanan | JWT umur pendek, RBAC + cek kepemilikan objek, sanitasi HTML SOP, audit append-only | Token di localStorage, otorisasi hanya di frontend |
| API | Versi API (`/api/v1`), ProblemDetails, paging di semua list, idempotency key untuk POST penting | Mengembalikan entitas database mentah |
| Frontend | Struktur per fitur, TanStack Query untuk server state, lazy loading route | Menyimpan data server di store global secara manual |
| DevOps | Image kecil non-root, scan CVE, deploy otomatis ke staging, approval ke prod, Helm/GitOps | Deploy manual, secret di repo |
| Observability | Log terstruktur + correlation id, trace OpenTelemetry, alert berbasis SLO | Log teks bebas tanpa konteks tenant/user |
| SOP | Versi immutable, pemisahan penulis/penyetuju, acknowledgement, review berkala | Mengedit SOP yang sudah terbit secara langsung |

### Keputusan yang Masih Perlu Ditetapkan

- [ ] Message broker: RabbitMQ (lebih sederhana) atau Kafka (volume event sangat tinggi)?
- [ ] Identity: bangun Auth Service sendiri (OpenIddict/Duende) atau pakai IdP perusahaan (Keycloak/Azure AD)?
- [ ] Lokasi deployment: Kubernetes on-premise/OpenShift atau cloud terkelola, sesuai kebijakan residensi data?
- [ ] Edisi & lisensi Oracle: apakah Partitioning dan Advanced Security (TDE) tersedia?
- [ ] Stack observability: ELK atau Grafana (Loki/Tempo/Prometheus)?
