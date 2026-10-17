# Rincian Story NexUS

Folder ini berisi **langkah implementasi dan acceptance criteria** untuk setiap story di `docs/SCRUM.md`. Satu file per epic.

| File | Epic | Story |
| --- | --- | --- |
| [E0-fondasi-devops.md](E0-fondasi-devops.md) | Fondasi & DevOps | NX-001 – NX-010 |
| [E1-auth-gateway.md](E1-auth-gateway.md) | Auth & Gateway | NX-011 – NX-017 |
| [E2-user-rbac.md](E2-user-rbac.md) | User & RBAC | NX-018 – NX-024 |
| [E3-project.md](E3-project.md) | Project Management | NX-025 – NX-031 |
| [E4-task.md](E4-task.md) | Task Management | NX-032 – NX-040 |
| [E5-dashboard.md](E5-dashboard.md) | Dashboard | NX-041 – NX-043 |
| [E6-sop-builder.md](E6-sop-builder.md) | SOP Builder | NX-044 – NX-054 |
| [E7-notification.md](E7-notification.md) | Notification | NX-055 – NX-058 |
| [E8-golive.md](E8-golive.md) | Audit, Observability & Go-live | NX-059 – NX-064 |

## Urutan Pengerjaan

Kerjakan sesuai nomor sprint di `SCRUM.md`. Di dalam satu sprint, ikuti kolom **Bergantung pada**: story tidak boleh dimulai sebelum dependency-nya ✅.

```
NX-001 → NX-002 → NX-003 → (NX-005, NX-007, NX-009) → NX-006 → NX-008, NX-010
       → NX-004 ─────────────────────────────────────────────┘
NX-011 → NX-012 → NX-013/014/015 → NX-016 → NX-017
NX-018 → NX-019 → NX-020 → NX-021..024
NX-025 → NX-026/027 → NX-028 → NX-029 → NX-030/031
NX-032 → NX-033/034 → NX-035..037 → NX-038..040 → NX-041/042
NX-044 → NX-045 → NX-046/047/048 → NX-049 → NX-050/051 → NX-052 → NX-053/054
NX-055 → NX-056 → NX-057 → NX-058
NX-059..062 → NX-063 → NX-064
```

## Siklus Kerja Satu Story

1. Baca rincian story di file epic-nya dan bagian `ARCHITECTURE.md` yang dirujuk.
2. `git checkout develop && git pull`, lalu `git checkout -b feature/NX-<id>-<deskripsi>`.
3. Ubah status di `SCRUM.md` menjadi 🟨.
4. Kerjakan langkah implementasi berurutan; tulis test bersamaan dengan kode.
5. Jalankan build, test, dan lint sampai hijau.
6. Centang acceptance criteria yang sudah terpenuhi di file epic.
7. Commit (`feat(<modul>): ... [NX-<id>]`), ubah status menjadi 🔍, buka PR ke `develop`.
8. Setelah merge: status ✅, perbarui tabel Epic dan progres keseluruhan di `SCRUM.md`.
