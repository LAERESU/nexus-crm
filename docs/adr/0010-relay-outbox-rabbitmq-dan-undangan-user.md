# ADR-0010: Relay Outbox ke RabbitMQ dan Undangan User lewat Token Aktivasi

- **Status:** Diterima
- **Tanggal:** 2026-11-13

## Konteks

NX-021 meminta Auth Service mengonsumsi `UserCreated` untuk membuat kredensial dan mengirim email undangan. Event sudah tersimpan di outbox User Service sejak NX-018, tetapi pengirimnya ke RabbitMQ baru dijadwalkan di NX-028. Tanpa pengirim itu NX-021 tidak bisa dipenuhi lewat event, dan memanggil Auth lewat REST akan menyimpang dari CONTEXT.md bagian 3 poin 2.

Story NX-028 menyebut MassTransit. Paket yang terpasang di repo, `MassTransit.RabbitMQ` 9.2.3, berlisensi komersial dan membutuhkan kunci lisensi saat dijalankan; versi 8 (Apache 2.0) tidak tersedia di cache paket.

## Keputusan

1. **Sebagian NX-028 dikerjakan sekarang**: relay outbox dan consumer idempotent di `BuildingBlocks.Messaging`, diterapkan di User Service (pengirim) dan Auth Service (penerima). NX-028 tinggal menerapkannya ke Project Service dan menambah kontrak event lain.
2. **`RabbitMQ.Client` langsung, bukan MassTransit**, di balik antarmuka sendiri (`IEventPublisher`, `IIntegrationEventHandler<T>`, `IOutboxEventMapper`). Referensi MassTransit dilepas dari `BuildingBlocks.Messaging`; barisnya di `Directory.Packages.props` dibiarkan sampai ada keputusan lisensi.
3. **Topologi:** satu topic exchange durable `nexus.events`, routing key = nama event (`UserCreated`), antrean durable per consumer `{service}.{NamaEvent}` (mis. `auth.UserCreated`). Pesan persistent, `MessageId` = `EventId`.
4. **Relay (`OutboxPublisherWorker<TContext>`):** tiap 2 detik mengambil sampai 50 baris dengan `FOR UPDATE SKIP LOCKED`, mengirim dengan publisher confirm, lalu menandai `PROCESSED_AT` dalam transaksi yang sama. Pengiriman at-least-once. Bila broker mati, baris tetap di outbox dan dicoba lagi tiap 10 detik.
5. **Kontrak terpisah dari domain event.** `IOutboxEventMapper` milik tiap service menerjemahkan baris outbox ke kontrak di `Messaging.Contracts`. Event tanpa kontrak hanya ditandai selesai. Saat ini User Service hanya mempublikasikan `UserCreated`.
6. **Consumer idempotent:** `PROCESSED_EVENTS (EVENT_ID, CONSUMER)` ditulis dalam transaksi yang sama dengan efeknya. Handler yang gagal mengembalikan pesan ke antrean setelah 5 detik; pesan yang tidak bisa dibaca dibuang dan dicatat.
7. **Undangan:** `UserCreated` → kredensial tanpa password (`PASSWORD_HASH` kini boleh kosong) dengan token aktivasi `{tenantId}.{256 bit acak}`; hanya hash SHA-256 yang disimpan, berlaku 72 jam, sekali pakai. `POST /api/v1/auth/activate` mengisi password pertama (minimal 10 karakter). Token salah, kedaluwarsa, dan sudah dipakai dijawab 422 dengan pesan yang sama. Login ke akun yang belum aktif dijawab seperti email tidak terdaftar.
8. **Email langsung lewat SMTP** (`Email:SmtpHost`, Mailpit di Development) dari dalam transaksi consumer: bila pengiriman gagal, kredensial tidak tersimpan dan event diulang. Dipindah ke Notification Service di NX-057.
9. **Tanpa broker service tetap hidup:** bila `ConnectionStrings:RabbitMq` kosong, relay dan consumer berhenti sendiri saat start dan event menumpuk di outbox.

## Konsekuensi

- Fitur MassTransit yang tidak dibangun ulang: dead-letter queue, retry bertingkat, saga. Pesan yang selalu gagal diproses akan berputar di antrean (tiap 5 detik) sampai ditangani; DLQ perlu ditambahkan di NX-028.
- Bila commit transaksi consumer gagal setelah email terkirim, user menerima dua email; hanya token dari email terakhir yang berlaku.
- Email login unik global (ADR-0006): undangan untuk email yang sudah dipakai kredensial lain dilewati dan dicatat sebagai peringatan.
- Belum ada kirim ulang undangan, dan halaman web `/activate` belum dibuat (tautan di email mengarah ke sana).
- Baris outbox yang sudah terproses belum dibersihkan.
