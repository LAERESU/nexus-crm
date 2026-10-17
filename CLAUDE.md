# CLAUDE.md — NexUS

Platform enterprise multi-tenant untuk manajemen proyek, tugas, dan SOP. Microservices ASP.NET Core 10 + Oracle + Redis + RabbitMQ + React. Bahasa komunikasi dan dokumentasi: **Bahasa Indonesia**.

## Wajib dibaca

Aturan lengkap proyek (arsitektur, konvensi kode, Git, keamanan):
@docs/CONTEXT.md

| File | Kapan dibaca |
| --- | --- |
| `docs/SCRUM.md` | Status progres, story aktif, sprint |
| `docs/stories/E*-*.md` | **Langkah implementasi + acceptance criteria** setiap story |
| `docs/ARCHITECTURE.md` | Desain teknis lengkap (schema, endpoint, contoh kode) |
| `docs/PLANNING.md` | Roadmap, milestone, keputusan terbuka D1–D6 |

Penamaan: contoh kode di `ARCHITECTURE.md` memakai `Company.BuildingBlocks.*` dan `Task.Domain`; di repo ini gunakan `NexUS.BuildingBlocks.*` dan `NexUS.<Service>.<Layer>` (mis. `NexUS.Task.Domain`).

## Cara bekerja di repo ini

1. Sebelum mulai, cek status story di `docs/SCRUM.md`, lalu baca rinciannya di `docs/stories/` dan bagian `ARCHITECTURE.md` yang dirujuk. Pastikan semua dependency-nya sudah ✅.
2. Kerjakan di branch `feature/NX-<id>-<deskripsi>` dari `develop`, bukan langsung di `main`.
3. Kerjakan hanya story yang diminta. Jika permintaan melanggar aturan di `docs/CONTEXT.md` bagian 3, sampaikan dulu sebelum mengerjakan.
4. Commit memakai Conventional Commits dan menyebut ID story, mis. `feat(task): tambah state machine status [NX-033]`.
5. Centang acceptance criteria yang terpenuhi di file `docs/stories/`, lalu perbarui status story di `docs/SCRUM.md` (🟨 berjalan, 🔍 review, ✅ selesai) beserta tabel Epic dan baris progres keseluruhan.
6. Jangan mengubah file migrasi di `db/` yang sudah di-commit, menambah dependency besar, atau mengubah struktur folder tanpa persetujuan.

## Perintah umum

```bash
# Infrastruktur lokal (Oracle Free, Redis, RabbitMQ)
cd deploy && docker compose up -d

# Backend (setelah solution dibuat di NX-002)
dotnet build
dotnet test

# Frontend (setelah NX-004)
cd web && npm install && npm run dev
```
