# ADR-0012: Outbox Bersama, Event Tertunda per Jenis, dan Dead-Letter Queue

- **Status:** Diterima
- **Tanggal:** 2026-11-14
- **Mengubah:** ADR-0010 (poin 3, 4, dan 6), ADR-0011 (konsekuensi relay tertahan)

## Konteks

NX-028 meminta outbox, relay, dan consumer idempotent sebagai komponen bersama, lalu dipakai Project Service untuk event proyek. ADR-0010 sudah membangun relay dan consumer, dengan dua kelemahan yang dicatat:

1. Publish bersifat `mandatory`: event yang belum punya antrean penerima dianggap gagal dan **menahan seluruh relay** (urutan dijaga). Project Service kini mempublikasikan event yang belum punya consumer sama sekali (Task Service belum ada), sehingga relay-nya akan berhenti selamanya pada event pertama.
2. Pesan yang selalu gagal diproses berputar di antrean tanpa batas (belum ada dead-letter queue).

Selain itu, kode outbox, audit, dan pemetaan tabel `OUTBOX_MESSAGES`/`PROCESSED_EVENTS` tersalin di DbContext Auth, User, dan Project.

Alternatif yang ditimbang lalu ditolak: **alternate exchange** yang memarkir event tanpa antrean di `nexus.unrouted`. Event yang diparkir tidak pernah sampai ke consumer yang mulai berjalan belakangan (mis. read model Task Service tidak akan berisi proyek lama), dan mengubah argumen exchange `nexus.events` membuat broker lama menolak deklarasi (`PRECONDITION_FAILED`) sehingga butuh langkah manual dan mengganggu rolling deploy.

## Keputusan

1. **Event tanpa antrean penerima tetap tertunda di outbox, per jenis event.** Publish tetap `mandatory` dengan publisher confirm. Bila broker mengembalikan pesan (`NO_ROUTE`), relay tidak menandainya terkirim, melewati jenis event itu selama 30 detik, dan terus mengirim jenis lain. Setelah consumer-nya pernah berjalan (antreannya ada), event tertunda ikut terkirim. Urutan hanya dijaga per jenis event.
2. **Dead-letter per antrean consumer.** Consumer yang gagal mengirim ulang pesan ke antrean yang sama dengan header `x-nexus-attempts` dan `x-nexus-last-error`; jeda 5 detik berlipat dua sampai 60 detik. Setelah 20 percobaan (sekitar 16 menit, cukup untuk gangguan database/SMTP sementara), atau bila pesan tidak bisa dibaca, pesan dipindah ke `{antrean}.dead`. Salinan dikirim dengan publisher confirm sebelum aslinya di-ack. Batas dan jeda bisa diatur di `Messaging:Consumer` (`MaxAttempts`, `RetryDelaySeconds`, `MaxRetryDelaySeconds`).
3. **Publisher** memakai ulang satu channel ber-confirm (dibuat ulang bila tertutup) dan membatasi satu publish 15 detik.
4. **Komponen bersama di `BuildingBlocks.Messaging`:**
   - `DbContext.PrepareSave(now, actor)`: kolom audit, `ROW_VERSION` (termasuk aggregate yang anaknya berubah), dan penyimpanan domain event ke outbox, dipakai Auth, User, dan Project.
   - `ModelBuilder.MapOutboxMessages()` dan `MapProcessedEvents()`.
   - `IdempotentEventHandler<TEvent, TContext>`: memeriksa dan mencatat `PROCESSED_EVENTS` dalam transaksi yang sama dengan efek event; nama consumer diambil dari registrasi `AddNexusEventConsumer` (sama dengan nama antrean).
   - `OutboxEventMapper`: dasar pemetaan domain event ke kontrak.
5. **Event yang dipublikasikan:** Project Service mengirim `ProjectCreated`, `ProjectUpdated`, `ProjectArchived`, `MemberAdded`, `MemberRoleChanged`, `MemberRemoved` (status/peran huruf kecil). User Service kini juga mengirim `RoleChanged` (alasan snake_case, mis. `user_roles_changed`). Event milestone belum punya kontrak dan tetap tertunda di outbox.
6. MassTransit tetap tidak dipakai (ADR-0010). Koneksi dan channel RabbitMQ ditutup dengan batas waktu saat service berhenti.

## Konsekuensi

- Baris outbox untuk jenis event yang belum punya consumer (saat ini event proyek dan `RoleChanged`) menumpuk sampai consumer-nya ada; relay mencoba tiap jenis itu sekali per 30 detik. Pembersihan outbox belum ada.
- Pengulangan memindahkan pesan ke belakang antrean dan event berbeda jenis bisa terkirim tidak berurutan; consumer harus tahan event tidak berurutan (seperti `USER_REF`).
- Pesan di antrean `.dead` belum ada alat pemrosesan ulang; harus dipindahkan manual (mis. lewat RabbitMQ management).
- Auth Service masih memakai pemeriksaan idempotensinya sendiri (`ProcessedEventStore`), karena consumer undangannya menyimpan kredensial dan mengirim email dalam satu transaksi lewat use case MediatR.
