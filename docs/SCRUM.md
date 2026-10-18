# NexUS — Scrum & Pelacak Progres

> File ini dipakai untuk melihat **sudah sampai mana pengembangan**. Langkah rinci setiap story ada di `docs/stories/`. Perbarui status story setiap hari, dan isi bagian Sprint Log di akhir setiap sprint.
> Terakhir diperbarui: 2026-10-17

---

## 1. Status Saat Ini

| Item | Nilai |
| --- | --- |
| Fase | 1 · Fondasi |
| Sprint aktif | Sprint 0 (persiapan) |
| Tanggal mulai sprint | — |
| Tanggal selesai sprint | — |
| Sprint goal | Menyiapkan repo, keputusan teknis awal, dan backlog siap dikerjakan |
| Progres keseluruhan | 12 / 64 story selesai (19%) |
| Hambatan aktif | — |

Legenda status: ⬜ Backlog · 📋 To Do (masuk sprint) · 🟨 In Progress · 🔍 Review · ✅ Done · 🟥 Blocked

## 2. Tim & Peran

| Peran | Nama | Tanggung jawab |
| --- | --- | --- |
| Product Owner | | Prioritas backlog, menerima hasil sprint |
| Scrum Master | | Memfasilitasi ceremony, menghilangkan hambatan |
| Tech Lead / Architect | | Keputusan arsitektur, code review, ADR |
| Backend Developer | | Service .NET, Oracle, Redis, messaging |
| Frontend Developer | | Aplikasi React |
| DevOps | | CI/CD, Docker, Kubernetes, monitoring |
| QA | | Test plan, test otomatis, UAT |

## 3. Aturan Sprint

| Aturan | Nilai |
| --- | --- |
| Durasi sprint | 2 minggu (10 hari kerja) |
| Estimasi | Story point Fibonacci (1, 2, 3, 5, 8, 13); story > 8 SP wajib dipecah |
| Velocity awal (asumsi) | 30–40 SP per sprint; disesuaikan setelah sprint 2 |
| Branch per story | `feature/NX-<id>-<deskripsi-singkat>` |

### Ceremony

| Ceremony | Kapan | Durasi | Output |
| --- | --- | --- | --- |
| Sprint Planning | Hari 1 sprint | 2 jam | Sprint goal + story terpilih di tabel sprint |
| Daily Standup | Setiap hari | 15 menit | Kemarin / hari ini / hambatan |
| Backlog Refinement | Pertengahan sprint | 1 jam | Story berikutnya memenuhi Definition of Ready |
| Sprint Review | Hari terakhir | 1 jam | Demo ke PO, story diterima/ditolak |
| Retrospective | Hari terakhir | 45 menit | 1–3 action item perbaikan |

### Definition of Ready (story siap dikerjakan)

- [ ] Judul berformat *Sebagai <peran>, saya ingin <aksi>, agar <manfaat>*
- [ ] Acceptance criteria jelas dan bisa diuji
- [ ] Dependency diketahui dan tidak memblokir
- [ ] Sudah diestimasi (≤ 8 SP)
- [ ] Desain UI/API tersedia bila diperlukan

### Definition of Done (story dianggap selesai)

- [ ] Kode di-merge ke `develop` lewat PR dengan minimal 1 approval
- [ ] Unit test ditulis dan lulus; coverage service tidak turun di bawah 70%
- [ ] Lint, build, dan scan keamanan di CI hijau
- [ ] Endpoint baru terdokumentasi di Swagger/OpenAPI
- [ ] Migrasi database (Flyway/Liquibase) disertakan bila ada perubahan skema
- [ ] Ter-deploy dan diuji di lingkungan dev
- [ ] Acceptance criteria diverifikasi QA/PO
- [ ] Status di file ini diperbarui menjadi ✅

## 4. Epic

| ID | Epic | Fase | Story | Selesai | Progres |
| --- | --- | --- | --- | --- | --- |
| E0 | Fondasi & DevOps | 1 | 10 | 9 | 90% |
| E1 | Auth & Gateway | 1 | 7 | 3 | 43% |
| E2 | User & RBAC | 2 | 7 | 0 | 0% |
| E3 | Project Management | 2 | 7 | 0 | 0% |
| E4 | Task Management | 2 | 9 | 0 | 0% |
| E5 | Dashboard | 2 | 3 | 0 | 0% |
| E6 | SOP Builder | 3 | 11 | 0 | 0% |
| E7 | Notification | 3 | 4 | 0 | 0% |
| E8 | Audit, Observability & Go-live | 4 | 6 | 0 | 0% |
| | **Total** | | **64** | **12** | **19%** |

## 5. Product Backlog

### E0 · Fondasi & DevOps

| ID | Story | SP | Sprint | Status |
| --- | --- | --- | --- | --- |
| NX-001 | Inisialisasi monorepo (struktur folder, `.editorconfig`, `.gitignore`, README) | 2 | 0 | ✅ |
| NX-002 | Solution .NET + BuildingBlocks (Common, Auth, Caching, Messaging, Observability) | 5 | 1 | ✅ |
| NX-003 | Template service Clean Architecture (Domain/Application/Infrastructure/Api + tests) | 5 | 1 | ✅ |
| NX-004 | Inisialisasi frontend React + Vite + TypeScript + lint + struktur fitur | 3 | 1 | ✅ |
| NX-005 | `docker-compose` lokal: Oracle Free, Redis, RabbitMQ, semua service | 5 | 1 | ✅ |
| NX-006 | Pipeline CI: build, test, lint, scan untuk backend & frontend | 5 | 1 | ✅ |
| NX-007 | Dockerfile multi-stage backend & frontend (non-root) | 3 | 1 | ✅ |
| NX-008 | Helm chart generik + deploy otomatis ke Kubernetes dev | 8 | 2 | ⬜ |
| NX-009 | Setup Flyway/Liquibase + user & schema Oracle per service | 5 | 1 | ✅ |
| NX-010 | Logging terstruktur (Serilog) + correlation id + health check | 3 | 2 | ✅ |

### E1 · Auth & Gateway

| ID | Story | SP | Sprint | Status |
| --- | --- | --- | --- | --- |
| NX-011 | API Gateway YARP dengan routing ke semua service | 3 | 2 | ✅ |
| NX-012 | Login username/password → access token + refresh token | 8 | 2 | ✅ |
| NX-013 | Refresh token dengan rotasi & deteksi reuse | 5 | 2 | ✅ |
| NX-014 | Logout + blacklist `jti` di Redis | 3 | 2 | 🔍 |
| NX-015 | Validasi JWT di Gateway & setiap service (JWKS) | 3 | 2 | ⬜ |
| NX-016 | Rate limiting di Gateway (login, per-user, per-tenant) | 5 | 2 | ⬜ |
| NX-017 | Halaman login React + interceptor refresh token | 5 | 2 | ⬜ |

### E2 · User & RBAC

| ID | Story | SP | Sprint | Status |
| --- | --- | --- | --- | --- |
| NX-018 | Schema & CRUD tenant, user, departemen | 8 | 3 | ⬜ |
| NX-019 | Role & permission + assign role ke user (dengan scope) | 8 | 3 | ⬜ |
| NX-020 | Permission policy dinamis + cache permission di Redis | 5 | 3 | ⬜ |
| NX-021 | Undang user via email (event `UserCreated`) | 3 | 3 | ⬜ |
| NX-022 | Halaman user list + form user | 5 | 3 | ⬜ |
| NX-023 | Halaman role + matriks permission | 5 | 3 | ⬜ |
| NX-024 | Proteksi route & komponen `<Can>` di React | 3 | 3 | ⬜ |

### E3 · Project Management

| ID | Story | SP | Sprint | Status |
| --- | --- | --- | --- | --- |
| NX-025 | Schema & CRUD proyek (soft delete, paging, filter) | 8 | 4 | ⬜ |
| NX-026 | Kelola anggota proyek & peran proyek | 5 | 4 | ⬜ |
| NX-027 | Milestone proyek | 3 | 4 | ⬜ |
| NX-028 | Outbox pattern + publish event proyek | 5 | 4 | ⬜ |
| NX-029 | Cache proyek & anggota di Redis | 3 | 4 | ⬜ |
| NX-030 | Halaman daftar proyek + form proyek | 5 | 4 | ⬜ |
| NX-031 | Halaman detail proyek (tab ringkasan, anggota, milestone) | 5 | 4 | ⬜ |

### E4 · Task Management

| ID | Story | SP | Sprint | Status |
| --- | --- | --- | --- | --- |
| NX-032 | Schema & CRUD tugas + subtask | 8 | 5 | ⬜ |
| NX-033 | State machine status tugas + riwayat status | 5 | 5 | ⬜ |
| NX-034 | Assignee + validasi keanggotaan proyek (read model) | 5 | 5 | ⬜ |
| NX-035 | Komentar tugas | 3 | 5 | ⬜ |
| NX-036 | Lampiran tugas (object storage + validasi file) | 5 | 5 | ⬜ |
| NX-037 | Checklist tugas | 3 | 5 | ⬜ |
| NX-038 | Kanban board drag-and-drop + optimistic update | 8 | 5 | ⬜ |
| NX-039 | Drawer detail tugas | 5 | 5 | ⬜ |
| NX-040 | Halaman "Tugas saya" | 3 | 5 | ⬜ |

### E5 · Dashboard

| ID | Story | SP | Sprint | Status |
| --- | --- | --- | --- | --- |
| NX-041 | Endpoint ringkasan dashboard (cache 1 menit) | 3 | 5 | ⬜ |
| NX-042 | Kartu statistik + grafik tugas per status | 5 | 5 | ⬜ |
| NX-043 | Widget tugas saya & SOP menunggu approval | 3 | 8 | ⬜ |

### E6 · SOP Builder

| ID | Story | SP | Sprint | Status |
| --- | --- | --- | --- | --- |
| NX-044 | Schema SOP (kategori, dokumen, versi, langkah) | 5 | 6 | ⬜ |
| NX-045 | CRUD kategori & dokumen SOP + kode otomatis | 5 | 6 | ⬜ |
| NX-046 | Editor TipTap + autosave draft | 8 | 6 | ⬜ |
| NX-047 | Sanitasi HTML konten SOP di server | 3 | 6 | ⬜ |
| NX-048 | Lock edit SOP (Redis) | 3 | 6 | ⬜ |
| NX-049 | Versioning (major/minor), immutable published, riwayat versi | 8 | 7 | ⬜ |
| NX-050 | Diff antar versi | 5 | 7 | ⬜ |
| NX-051 | Alur approval berjenjang per kategori + segregation of duties | 8 | 7 | ⬜ |
| NX-052 | Publish SOP + event `SopPublished` | 3 | 7 | ⬜ |
| NX-053 | Acknowledgement + laporan siapa yang belum membaca | 5 | 8 | ⬜ |
| NX-054 | Tautkan SOP ke proyek + langkah SOP menjadi checklist tugas | 8 | 8 | ⬜ |

### E7 · Notification

| ID | Story | SP | Sprint | Status |
| --- | --- | --- | --- | --- |
| NX-055 | Consumer event + simpan notifikasi (idempotent) | 5 | 6 | ⬜ |
| NX-056 | Push real-time SignalR (backplane Redis) + bell di React | 5 | 6 | ⬜ |
| NX-057 | Email notifikasi dengan template + retry | 5 | 7 | ⬜ |
| NX-058 | Preferensi notifikasi per user | 3 | 8 | ⬜ |

### E8 · Audit, Observability & Go-live

| ID | Story | SP | Sprint | Status |
| --- | --- | --- | --- | --- |
| NX-059 | Audit Service + tabel audit berpartisi + interceptor EF Core | 8 | 9 | ⬜ |
| NX-060 | OpenTelemetry trace & metrik + dashboard Grafana | 5 | 9 | ⬜ |
| NX-061 | Alerting (error rate, latency, pod restart, Redis/Oracle down) | 3 | 9 | ⬜ |
| NX-062 | Load test (k6) & perbaikan performa | 5 | 9 | ⬜ |
| NX-063 | Security test (OWASP ZAP), pentest, hardening | 8 | 10 | ⬜ |
| NX-064 | UAT, runbook deploy/rollback, rilis produksi | 5 | 10 | ⬜ |

## 6. Rencana Sprint

| Sprint | Fase | Sprint goal | Story | Status |
| --- | --- | --- | --- | --- |
| 0 | Persiapan | Repo awal, keputusan D1–D6, tim, backlog siap | NX-001 | 🟨 |
| 1 | Fondasi | Template service, docker-compose, CI hijau | NX-002–007, NX-009 | ⬜ |
| 2 | Fondasi | Login end-to-end via Gateway, deploy otomatis ke dev | NX-008, NX-010–017 | ⬜ |
| 3 | Layanan inti | User & RBAC berjalan | NX-018–024 | ⬜ |
| 4 | Layanan inti | Proyek & anggota berjalan | NX-025–031 | ⬜ |
| 5 | Layanan inti | Tugas, kanban, dashboard dasar | NX-032–042 | ⬜ |
| 6 | SOP Builder | Editor SOP + notifikasi in-app | NX-044–048, NX-055–056 | ⬜ |
| 7 | SOP Builder | Versioning, approval, publish, email | NX-049–052, NX-057 | ⬜ |
| 8 | SOP Builder | Acknowledgement, SOP → checklist, widget | NX-043, NX-053–054, NX-058 | ⬜ |
| 9 | Go-live | Audit, observability, load test | NX-059–062 | ⬜ |
| 10 | Go-live | Security, UAT, rilis | NX-063–064 | ⬜ |

Catatan: sprint 2 dan 5 cukup padat (> 40 SP). Setelah velocity nyata diketahui di sprint 2, geser story yang tidak muat ke sprint berikutnya.

## 7. Sprint Log

Salin template di bawah untuk setiap sprint yang selesai, terbaru di atas.

<!--
### Sprint N — YYYY-MM-DD s.d. YYYY-MM-DD

**Sprint goal:** ...
**Tercapai?** Ya / Sebagian / Tidak

| Metrik | Nilai |
| --- | --- |
| SP direncanakan | |
| SP selesai | |
| Story selesai | |
| Story dibawa ke sprint berikutnya | |

**Selesai:** NX-xxx, NX-yyy
**Belum selesai (alasan):** NX-zzz — ...
**Hambatan:** ...
**Hasil review (catatan PO):** ...
**Retro — pertahankan:** ...
**Retro — perbaiki (action item + PIC):** ...
-->

_Belum ada sprint yang selesai._

## 8. Hambatan & Isu

| Tanggal | Isu | Dampak (story) | PIC | Status |
| --- | --- | --- | --- | --- |
| | | | | |

## 9. Riwayat Velocity

| Sprint | SP direncanakan | SP selesai | Catatan |
| --- | --- | --- | --- |
| | | | |
