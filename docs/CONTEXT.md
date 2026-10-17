# NexUS — Konteks & Aturan Proyek

> Baca file ini sebelum mulai bekerja di repo. Isinya konteks proyek, aturan arsitektur, konvensi kode, dan alur kerja yang wajib diikuti developer maupun AI assistant.
> Terakhir diperbarui: 2026-10-17

---

## 1. Konteks Singkat

- **Produk:** platform enterprise multi-tenant untuk manajemen proyek, tugas, dan SOP perusahaan.
- **Pengguna:** karyawan, manajer proyek, penulis SOP, reviewer/approver SOP, admin tenant, auditor.
- **Stack:** ASP.NET Core 10 (microservices) · Oracle Database · Redis 7 · RabbitMQ · React 18 + TypeScript · Docker · Kubernetes · GitHub Actions.
- **Dokumen terkait:**
  - `docs/PLANNING.md`: ruang lingkup, roadmap, milestone, keputusan terbuka
  - `docs/SCRUM.md`: backlog, sprint, dan progres (**sumber kebenaran status**)
  - `docs/stories/`: langkah implementasi dan acceptance criteria per story
  - `docs/ARCHITECTURE.md`: desain teknis lengkap
  - `docs/adr/`: catatan keputusan arsitektur

## 2. Struktur Repo

```
NexUS/
├── docs/                  # PLANNING.md, SCRUM.md, CONTEXT.md, adr/
├── src/
│   ├── BuildingBlocks/    # kode bersama (Common, Auth, Caching, Messaging, Observability)
│   ├── ApiGateway/        # Gateway.Api (YARP)
│   └── Services/
│       ├── Auth/  User/  Project/  Task/  Sop/  Notification/  Audit/
│       │   └── <Nama>.Domain / .Application / .Infrastructure / .Api / tests/
├── web/                   # aplikasi React
├── db/
│   └── <schema>/          # migrasi Flyway: V1__init.sql, V2__...
├── deploy/
│   ├── docker-compose.yml   # Oracle Free, Redis, RabbitMQ
│   ├── .env.example
│   └── helm/
└── .github/workflows/
```

## 3. Aturan Arsitektur (WAJIB)

1. **Tidak ada shared database.** Setiap service hanya mengakses schema Oracle miliknya sendiri. Dilarang `JOIN` atau `SELECT` ke schema service lain.
2. **Komunikasi antar service:**
   - butuh jawaban langsung → REST/gRPC dengan timeout, retry, dan circuit breaker (Polly);
   - memberi tahu service lain → event lewat RabbitMQ dengan **Outbox pattern**.
3. **Consumer event harus idempotent.** Simpan `eventId` yang sudah diproses.
4. **Data milik service lain** yang sering dibaca disimpan sebagai *read model* lokal dan diperbarui dari event.
5. **Arah dependency Clean Architecture:** `Api → Application → Domain`, sedangkan `Infrastructure → Application/Domain`. Domain tidak boleh bergantung pada apa pun.
6. **Aturan bisnis ada di Domain**, bukan di controller atau repository.
7. **Redis hanya cache**, bukan sumber kebenaran. Pakai pola cache-aside, TTL wajib, hapus key saat data berubah, dan aplikasi tetap berjalan bila Redis mati.
8. **Multi-tenant:** setiap query wajib memfilter `TENANT_ID`, dan setiap cache key wajib berawalan `tnt:{tenantId}:`.
9. **Versi SOP yang sudah `PUBLISHED` tidak boleh diubah.** Perubahan selalu membuat versi baru.
10. Keputusan arsitektur baru atau perubahan aturan di atas wajib dicatat sebagai ADR di `docs/adr/`.

## 4. Konvensi Backend (.NET)

| Aspek | Aturan |
| --- | --- |
| Versi | .NET 10 LTS, `Nullable` enable, `TreatWarningsAsErrors` di CI |
| Penamaan | PascalCase untuk class/method/property, camelCase untuk variabel lokal, `_camelCase` untuk field privat, prefix `I` untuk interface |
| Use case | Satu folder per command/query: `Command`, `Validator`, `Handler` (MediatR) |
| Validasi | FluentValidation di Application layer (wajib), bukan di controller |
| Akses data | EF Core untuk tulis, Dapper untuk query baca kompleks |
| SQL | Selalu bind variable (`:param`); dilarang merangkai SQL dengan string |
| Error | Exception domain → ProblemDetails (400 validasi, 403 akses, 404, 409 konkurensi, 422 aturan bisnis) |
| Async | Semua I/O `async` dan meneruskan `CancellationToken` |
| Konfigurasi | `appsettings.json` + environment variable; secret tidak boleh ada di repo |
| Logging | Serilog terstruktur, pakai template (`{TaskId}`), bukan interpolasi string; jangan log password, token, atau data pribadi |
| Tes | xUnit + FluentAssertions; integration test memakai Testcontainers; coverage ≥ 70% |

## 5. Konvensi Database (Oracle)

| Aspek | Aturan |
| --- | --- |
| Penamaan | `UPPER_SNAKE_CASE`; nama tabel jamak; PK `ID`; FK `<ENTITAS>_ID`; index `IX_<TABEL>_<KOLOM>`, unique `UX_`/`UQ_` |
| Kolom wajib | `TENANT_ID`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`, `ROW_VERSION`, `IS_DELETED` |
| ID publik | `PUBLIC_ID RAW(16) DEFAULT SYS_GUID()`; ID internal tidak diekspos ke API |
| Waktu | `TIMESTAMP WITH TIME ZONE` dalam UTC |
| Migrasi | Hanya lewat Flyway (`db/<schema>/V<n>__<deskripsi>.sql`). File yang sudah di-merge **tidak boleh diubah**; perbaikan dibuat sebagai migrasi baru |
| Index | Semua FK diberi index; `TENANT_ID` di depan untuk index query utama |
| Hapus data | Soft delete (`IS_DELETED = 1`), kecuali data sementara |

## 6. Konvensi API

- Prefix dan versi: `/api/v1/<resource>`; resource memakai kata benda jamak dan kebab-case.
- Method: `GET` baca, `POST` buat (201 + header `Location`), `PUT` ganti, `PATCH` ubah sebagian atau ubah status, `DELETE` hapus (204).
- Semua endpoint list wajib memakai paging: `?page=1&size=20` (maksimum 100) dan mengembalikan `{ items, page, size, totalCount }`.
- Konkurensi memakai header `If-Match` berisi `ROW_VERSION`; bila tidak cocok, kembalikan 409.
- JSON menggunakan camelCase dan tanggal ISO 8601 UTC.
- Setiap endpoint wajib punya `[Authorize(Policy = "<modul>.<aksi>")]`, kecuali endpoint auth publik.
- Header `X-Correlation-Id` diteruskan ke semua log dan panggilan antar service.
- Setiap endpoint terdokumentasi di Swagger/OpenAPI.

## 7. Konvensi Frontend (React)

| Aspek | Aturan |
| --- | --- |
| Bahasa | TypeScript `strict`; hindari `any` |
| Struktur | Per fitur di `src/features/<fitur>/` (`api/`, `components/`, `hooks/`, `pages/`, `store/`) |
| Server state | TanStack Query; query key didefinisikan di satu objek per fitur |
| Client state | Zustand, hanya untuk state UI dan sesi |
| Form | React Hook Form + Zod |
| HTTP | Hanya lewat `shared/api/httpClient.ts`; komponen tidak boleh memanggil `axios`/`fetch` langsung |
| Token | Access token disimpan di memori; refresh token di cookie httpOnly. **Dilarang** memakai `localStorage` untuk token |
| Akses | `<Can permission>` hanya untuk tampilan; otorisasi tetap di backend |
| Penamaan | Komponen PascalCase (`TaskCard.tsx`), hook `useXxx`, file lain camelCase |
| HTML | `dangerouslySetInnerHTML` hanya untuk konten SOP yang sudah disanitasi server |
| Tes | Vitest + Testing Library; E2E memakai Playwright untuk alur utama |

## 8. Aturan Keamanan

- JWT RS256, access token berlaku 15 menit, refresh token 7 hari dengan rotasi.
- Selain permission, periksa **kepemilikan objek** (mis. user adalah anggota proyek).
- Penulis SOP tidak boleh menyetujui SOP-nya sendiri.
- Upload file dibatasi ukuran dan tipenya (cek magic bytes), lalu dipindai antivirus.
- Secret hanya disimpan di Vault atau Kubernetes Secret.
- Semua perubahan penting dicatat di audit log.
- Dependency dan image dipindai di CI; temuan HIGH/CRITICAL menggagalkan build.

## 9. Alur Kerja Git

| Aspek | Aturan |
| --- | --- |
| Branch utama | `main` (produksi), `develop` (integrasi) |
| Branch kerja | `feature/NX-<id>-<deskripsi>`, `fix/NX-<id>-<deskripsi>`, `hotfix/<deskripsi>`, `chore/<deskripsi>` |
| Commit | Conventional Commits: `feat(task): tambah state machine status [NX-033]` |
| Tipe commit | `feat`, `fix`, `refactor`, `test`, `docs`, `chore`, `ci`, `perf` |
| Pull request | Ke `develop`, minimal 1 approval, CI wajib hijau, squash merge |
| Isi PR | ID story, ringkasan perubahan, cara menguji, screenshot untuk perubahan UI |
| Rilis | `develop` → `main` lewat PR rilis, diberi tag semver `vX.Y.Z` |

## 10. Cara Memperbarui Progres

1. Saat mulai mengerjakan story, ubah statusnya di `docs/SCRUM.md` menjadi 🟨.
2. Saat PR dibuka, ubah menjadi 🔍; saat memenuhi Definition of Done, ubah menjadi ✅.
3. Perbarui kolom **Selesai** dan **Progres** di tabel Epic serta baris "Progres keseluruhan" di bagian Status Saat Ini.
4. Di akhir sprint, isi Sprint Log dan Riwayat Velocity.
5. Bila milestone tercapai, perbarui tabel Milestone di `docs/PLANNING.md`.
6. Commit perubahan dokumen dengan pesan `docs(progress): sprint N update`.

## 11. Aturan untuk AI Assistant

Jika AI assistant (Claude, Copilot, dll.) bekerja di repo ini:

- Baca `CONTEXT.md`, lalu `SCRUM.md`, untuk mengetahui story yang sedang aktif.
- Kerjakan hanya story yang diminta, dan sebutkan ID story-nya di commit.
- Patuhi aturan arsitektur di bagian 3. Jika sebuah permintaan melanggarnya, sampaikan dulu sebelum mengerjakan.
- Jangan mengubah file migrasi yang sudah ada, menambah dependency besar, atau mengubah struktur folder tanpa persetujuan.
- Setelah selesai, perbarui status story di `SCRUM.md` dan sebutkan hal yang belum selesai.
