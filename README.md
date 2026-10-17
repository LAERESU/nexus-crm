<p align="center"><img src="Logo_NexUS.svg" alt="NexUS" width="160"></p>

# NexUS

[![ci-backend](https://github.com/LAERESU/nexus-crm/actions/workflows/ci-backend.yml/badge.svg?branch=develop)](https://github.com/LAERESU/nexus-crm/actions/workflows/ci-backend.yml)
[![ci-frontend](https://github.com/LAERESU/nexus-crm/actions/workflows/ci-frontend.yml/badge.svg?branch=develop)](https://github.com/LAERESU/nexus-crm/actions/workflows/ci-frontend.yml)
[![ci-database](https://github.com/LAERESU/nexus-crm/actions/workflows/ci-database.yml/badge.svg?branch=develop)](https://github.com/LAERESU/nexus-crm/actions/workflows/ci-database.yml)
[![ci-images](https://github.com/LAERESU/nexus-crm/actions/workflows/ci-images.yml/badge.svg?branch=develop)](https://github.com/LAERESU/nexus-crm/actions/workflows/ci-images.yml)
[![codeql](https://github.com/LAERESU/nexus-crm/actions/workflows/codeql.yml/badge.svg?branch=develop)](https://github.com/LAERESU/nexus-crm/actions/workflows/codeql.yml)

Platform enterprise multi-tenant untuk **manajemen proyek, tugas, dan SOP perusahaan**. Dibangun dengan arsitektur microservices: ASP.NET Core 10, Oracle Database, Redis, RabbitMQ, dan React.

## Dokumentasi

| File | Isi |
| --- | --- |
| [docs/CONTEXT.md](docs/CONTEXT.md) | Konteks proyek, aturan arsitektur, konvensi kode, alur Git. **Baca ini terlebih dahulu.** |
| [docs/PLANNING.md](docs/PLANNING.md) | Ruang lingkup, roadmap, milestone, keputusan terbuka, risiko |
| [docs/SCRUM.md](docs/SCRUM.md) | Backlog, sprint, dan **status progres** |
| [docs/stories/](docs/stories/) | Langkah implementasi & acceptance criteria per story |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Desain teknis lengkap |
| [docs/adr/](docs/adr/) | Catatan keputusan arsitektur (ADR) |

## Struktur Repo

```
NexUS/
├── docs/                # dokumen perencanaan & ADR
├── src/
│   ├── BuildingBlocks/  # kode bersama
│   ├── ApiGateway/      # YARP gateway
│   └── Services/        # Auth, User, Project, Task, Sop, Notification, Audit
├── web/                 # aplikasi React
├── db/<schema>/         # migrasi Flyway per schema Oracle
├── deploy/              # docker-compose & Helm chart
└── .github/             # workflow CI/CD & template PR
```

## Menjalankan Lingkungan Lokal

Prasyarat: Docker Desktop (disarankan alokasi memori minimal 6 GB).

```bash
cd deploy
cp .env.example .env                 # isi password sesuai kebutuhan
docker compose up -d --build         # infrastruktur + 7 service + web
```

Hanya infrastruktur (service dan web dijalankan dari IDE dengan `dotnet run` / `npm run dev`):

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d
```

| Layanan | Alamat |
| --- | --- |
| Oracle Free | `localhost:1521/FREEPDB1` (user schema: `AUTH_SCHEMA`, `USR_SCHEMA`, `PRJ_SCHEMA`, `TSK_SCHEMA`, `SOP_SCHEMA`, `NTF_SCHEMA`, `AUD_SCHEMA`) |
| Redis | `localhost:6379` |
| RabbitMQ | `localhost:5672`, UI di http://localhost:15672 |
| RustFS (S3) | `localhost:9000`, console di http://localhost:9001/rustfs/console/index.html |
| Mailpit | SMTP `localhost:1025`, UI di http://localhost:8025 |
| API Gateway | http://localhost:5100 (`/api/v1/<resource>`, `/api/v1/<resource>/health`) |
| Service Auth, User, Project, Task, Sop, Notification, Audit | http://localhost:5101 … 5107 (`/health/live`, `/swagger`) |
| Web | http://localhost:3000 |

Migrasi database dijalankan otomatis oleh container `flyway` (`db/<schema>/V<n>__*.sql`). Jalankan manual dengan `scripts/db-migrate.sh [migrate|validate|info]` atau `scripts/db-migrate.ps1 -Command info`. File migrasi yang sudah di-merge tidak boleh diubah; buat migrasi baru.

Oracle membutuhkan beberapa menit saat pertama kali dijalankan. Cek dengan `docker compose ps` hingga semua container `healthy`. Hapus data lokal dengan `docker compose down -v`.

## Status

Lihat [docs/SCRUM.md](docs/SCRUM.md) bagian **Status Saat Ini**.
