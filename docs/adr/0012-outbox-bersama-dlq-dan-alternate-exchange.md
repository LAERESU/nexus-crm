# ADR-0012: Outbox Bersama, Dead-Letter Queue, dan Alternate Exchange

- **Status:** Diterima
- **Tanggal:** 2026-11-14
- **Mengubah:** ADR-0010 (poin 3 dan 6), ADR-0011 (konsekuensi relay tertahan)

## Konteks

NX-028 meminta outbox, relay, dan consumer idempotent sebagai komponen bersama, lalu dipakai Project Service untuk event proyek. ADR-0010 sudah membangun relay dan consumer, dengan dua kelemahan yang dicatat:

1. Publish bersifat `mandatory`: event yang belum punya antrean penerima dianggap gagal dan **menahan relay** (urutan dijaga). Project Service kini mempublikasikan event yang belum punya consumer sama sekali (Task Service belum ada), sehingga relay-nya akan berhenti selamanya pada event pertama.
2. Pesan yang selalu gagal diproses berputar di antrean tanpa batas (belum ada dead-letter queue).

Selain itu, kode outbox, audit, dan pemetaan tabel `OUTBOX_MESSAGES`/`PROCESSED_EVENTS` tersalin di DbContext Auth, User, dan Project.

## Keputusan

1. **Alternate exchange.** `nexus.events` dideklarasikan dengan argumen `alternate-exchange = nexus.unrouted` (fanout) yang terikat ke antrean durable `nexus.unrouted` dengan TTL 7 hari. Event tanpa antrean penerima diparkir di sana dan outbox ditandai terkirim. Publish tetap `mandatory` dengan publisher confirm (hanya gagal bila alternate exchange pun tidak ada), dan dibatasi 15 detik per publish.
2. **Dead-letter per antrean consumer.** Consumer yang gagal mengirim ulang pesan ke antrean yang sama setelah 5 detik dengan header `x-nexus-attempts` dan `x-nexus-last-error`. Setelah 5 percobaan, atau bila pesan tidak bisa dibaca, pesan dipindah ke `{antrean}.dead` (bukan dibuang).
3. **Komponen bersama di `BuildingBlocks.Messaging`:**
   - `DbContext.PrepareSave(now, actor)`: kolom audit, `ROW_VERSION` (termasuk aggregate yang anaknya berubah), dan penyimpanan domain event ke outbox, dipakai Auth, User, dan Project.
   - `ModelBuilder.MapOutboxMessages()` dan `MapProcessedEvents()`.
   - `IdempotentEventHandler<TEvent, TContext>`: memeriksa dan mencatat `PROCESSED_EVENTS` dalam transaksi yang sama dengan efek event.
   - `OutboxEventMapper`: dasar pemetaan domain event ke kontrak.
4. **Event yang dipublikasikan:** Project Service mengirim `ProjectCreated`, `ProjectUpdated`, `ProjectArchived`, `MemberAdded`, `MemberRoleChanged`, `MemberRemoved`. User Service kini juga mengirim `RoleChanged`. Event milestone belum punya kontrak dan tetap tertunda di outbox.
5. MassTransit tetap tidak dipakai (ADR-0010).

## Konsekuensi

- Event yang tiba sebelum consumer-nya pernah berjalan **tidak** dikirim ulang otomatis ke consumer itu; ia diparkir di `nexus.unrouted` selama 7 hari dan harus dipindahkan manual (mis. lewat RabbitMQ management) bila dibutuhkan. Sebelumnya event itu menunggu di outbox. Ini terutama berlaku untuk `UserCreated` bila Auth Service belum pernah berjalan saat user dibuat; di deployment normal semua service mendeklarasikan antreannya saat start.
- Broker yang sudah punya `nexus.events` tanpa alternate exchange (mis. RabbitMQ lokal dari sprint sebelumnya) menolak deklarasi baru (`PRECONDITION_FAILED`). Hapus exchange `nexus.events` sekali (management UI atau `rabbitmqctl`) lalu jalankan ulang service.
- Pengulangan memindahkan pesan ke belakang antrean, jadi urutan pemrosesan tidak dijamin; consumer harus tahan event tidak berurutan (seperti `USER_REF`).
- Pesan di antrean `.dead` belum ada alat pemrosesan ulang; harus dipindahkan manual.
- Koneksi RabbitMQ ditutup dengan batas waktu saat service berhenti, karena koneksi ke broker yang sempat macet bisa tidak pernah menjawab.
