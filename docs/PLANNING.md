# NexUS — Perencanaan Proyek

> Dokumen acuan tingkat tinggi. Detail teknis lengkap ada di `docs/ARCHITECTURE.md`; langkah per story ada di `docs/stories/`.
> Terakhir diperbarui: 2026-10-17

---

## 1. Ringkasan

NexUS adalah platform enterprise multi-tenant untuk **manajemen proyek, tugas, dan SOP**. Setiap perusahaan (tenant) dapat menulis, me-review, menyetujui, dan memversikan SOP sendiri, lalu menautkannya ke proyek dan tugas sebagai checklist.

| Lapisan | Teknologi |
| --- | --- |
| Frontend | React 18 + TypeScript + Vite, React Router, Zustand, TanStack Query, TipTap |
| API Gateway | YARP (ASP.NET Core) |
| Backend | ASP.NET Core 10, Clean Architecture + DDD, MediatR, FluentValidation |
| Data access | EF Core (Oracle provider) untuk tulis, Dapper untuk baca |
| Database | Oracle Database (1 schema per service) |
| Cache | Redis 7 |
| Messaging | RabbitMQ (MassTransit) + Outbox pattern |
| Observability | Serilog, OpenTelemetry, Grafana/ELK |
| Deployment | Docker, Kubernetes, Helm, GitHub Actions |

## 2. Tujuan

1. Mengelola proyek dan tugas lintas tim dalam satu platform multi-tenant.
2. Menyediakan SOP terkontrol (draft → review → approved → published) dengan jejak audit lengkap.
3. Setiap service bisa di-deploy dan di-scale secara terpisah, aman untuk lingkungan enterprise.

## 3. Ruang Lingkup

### Termasuk (MVP)
- Auth (login, refresh token, logout, RBAC)
- User & role management per tenant
- Project management (proyek, anggota, milestone)
- Task management (kanban, assignee, komentar, lampiran, checklist)
- SOP Builder (editor rich text, langkah, versioning, approval berjenjang, acknowledgement)
- Notifikasi in-app (real-time) dan email
- Audit log
- Dashboard ringkasan

### Tidak termasuk (setelah MVP)
- Aplikasi mobile native
- Kolaborasi real-time multi-penulis di editor SOP
- Integrasi WhatsApp/Teams
- Reporting/BI lanjutan
- Billing tenant

## 4. Daftar Service

| Service | Schema Oracle | Tanggung jawab |
| --- | --- | --- |
| Gateway | — | Routing, validasi JWT, rate limiting, CORS |
| Auth | `AUTH_SCHEMA` | Login, token, refresh, revoke, MFA |
| User | `USR_SCHEMA` | Tenant, user, departemen, role, permission |
| Project | `PRJ_SCHEMA` | Proyek, anggota, milestone, tautan SOP |
| Task | `TSK_SCHEMA` | Tugas, assignee, status, komentar, lampiran, checklist |
| SOP | `SOP_SCHEMA` | Kategori, dokumen, versi, langkah, approval, acknowledgement |
| Notification | `NTF_SCHEMA` | Notifikasi in-app, email, SignalR |
| Audit | `AUD_SCHEMA` | Penyimpanan audit log dari semua service |

## 5. Roadmap & Gate

Estimasi dengan asumsi tim 4–6 developer dan sprint 2 minggu. Fase berikutnya baru dimulai setelah gate fase sebelumnya terpenuhi.

| Fase | Sprint | Fokus | Gate (syarat lanjut) |
| --- | --- | --- | --- |
| 1 · Fondasi | 1–2 | Repo, BuildingBlocks, CI/CD, Docker, K8s dev, Gateway, Auth, schema Oracle awal | Pipeline jalan otomatis sampai deploy ke dev; login end-to-end berhasil |
| 2 · Layanan inti | 3–5 | User + RBAC, Project, Task + kanban, dashboard dasar | Alur buat proyek → buat tugas → assign → selesai lulus UAT internal |
| 3 · SOP Builder | 6–8 | Editor, versioning, approval berjenjang, notifikasi, SOP → checklist tugas | Alur SOP draft → approve → publish → acknowledge lulus uji |
| 4 · Go-live | 9–10 | Audit log lengkap, monitoring & alert, load test, security test, UAT, rilis bertahap | Semua kriteria siap produksi (bagian 7) terpenuhi |

## 6. Milestone

| ID | Milestone | Target sprint | Status |
| --- | --- | --- | --- |
| M1 | Repo & pipeline CI/CD berjalan | Sprint 1 | ⬜ Belum |
| M2 | Login & JWT end-to-end via Gateway | Sprint 2 | ⬜ Belum |
| M3 | User & RBAC selesai | Sprint 3 | ⬜ Belum |
| M4 | Project & Task (kanban) selesai | Sprint 5 | ⬜ Belum |
| M5 | SOP editor + versioning | Sprint 7 | ⬜ Belum |
| M6 | Approval SOP + notifikasi + checklist | Sprint 8 | ⬜ Belum |
| M7 | Observability & hardening keamanan | Sprint 9 | ⬜ Belum |
| M8 | UAT selesai & go-live | Sprint 10 | ⬜ Belum |

Legenda status: ⬜ Belum · 🟨 Berjalan · ✅ Selesai · 🟥 Terhambat

## 7. Kriteria Siap Produksi

- [ ] Semua service punya health check `/health/live` dan `/health/ready`
- [ ] Coverage unit test ≥ 70% per service
- [ ] Tidak ada CVE HIGH/CRITICAL yang bisa diperbaiki (Trivy, dependency scan)
- [ ] Pentest / DAST (OWASP ZAP) tanpa temuan high yang terbuka
- [ ] Load test: p95 latency < 800 ms pada beban target
- [ ] Dashboard monitoring & alert aktif (error rate, latency, pod restart, Redis/Oracle down)
- [ ] Backup & restore Oracle sudah diuji
- [ ] Runbook deploy & rollback tersedia
- [ ] UAT ditandatangani product owner

## 8. Keputusan Terbuka

| ID | Pertanyaan | Opsi | Keputusan | Tanggal |
| --- | --- | --- | --- | --- |
| D1 | Message broker | RabbitMQ / Kafka | (default: RabbitMQ) | |
| D2 | Identity provider | Auth Service sendiri (OpenIddict) / Keycloak / Azure AD | Auth Service sendiri, JWT RS256 via Microsoft.IdentityModel (ADR-0006) | 2026-10-18 |
| D3 | Lokasi deployment | K8s on-prem / OpenShift / cloud terkelola | | |
| D4 | Lisensi Oracle | Partitioning & TDE tersedia? | | |
| D5 | Stack observability | Grafana (Loki/Tempo/Prometheus) / ELK | | |
| D6 | Object storage lampiran | MinIO / S3 / Azure Blob | | |

Setiap keputusan yang sudah diambil dicatat sebagai ADR di `docs/adr/NNNN-judul.md`.

## 9. Risiko

| Risiko | Dampak | Mitigasi |
| --- | --- | --- |
| Microservices terlalu kompleks untuk tim kecil | Pengembangan lambat | Mulai dengan BuildingBlocks yang kuat; service sederhana pakai struktur tipis |
| Lisensi fitur Oracle tidak tersedia | Partitioning/TDE tidak bisa dipakai | Pastikan D4 di sprint 1; siapkan purge berbasis job sebagai alternatif |
| Konsistensi data antar service | Data tidak sinkron | Outbox pattern + consumer idempotent + read model |
| Editor SOP kompleks | Fase 3 molor | Pakai TipTap + ekstensi standar dulu; fitur lanjutan setelah MVP |
| Kebutuhan keamanan enterprise berubah | Rework | Libatkan tim security sejak fase 1 |
