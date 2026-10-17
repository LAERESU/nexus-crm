<p align="center"><img src="Logo_NexUS.svg" alt="NexUS" width="160"></p>

# NexUS

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

## Menjalankan Infrastruktur Lokal

Prasyarat: Docker Desktop.

```bash
cd deploy
cp .env.example .env      # isi password sesuai kebutuhan
docker compose up -d      # Oracle Free, Redis, RabbitMQ
```

| Layanan | Alamat |
| --- | --- |
| Oracle Free | `localhost:1521/FREEPDB1` |
| Redis | `localhost:6379` |
| RabbitMQ | `localhost:5672`, UI di http://localhost:15672 |

Oracle membutuhkan beberapa menit saat pertama kali dijalankan. Cek dengan `docker compose ps` hingga statusnya `healthy`.

## Status

Lihat [docs/SCRUM.md](docs/SCRUM.md) bagian **Status Saat Ini**.
