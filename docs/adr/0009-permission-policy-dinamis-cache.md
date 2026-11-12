# ADR-0009: Policy Permission Dinamis dan Cache Permission di Redis

- **Status:** Diterima
- **Tanggal:** 2026-11-08

## Konteks

NX-020 mengganti pemetaan sementara policy ke role `admin` di token (ADR-0006) dengan pemeriksaan permission sungguhan (`user.manage`, `role.manage`, dst.). Data permission dimiliki User Service, sedangkan semua service perlu memeriksanya di setiap request. Belum ada relay outbox maupun RabbitMQ (NX-028), jadi consumer `RoleChanged` yang disebut di story belum bisa dibuat.

## Keputusan

1. **Policy dinamis.** `PermissionPolicyProvider` membuat policy untuk setiap nama berformat `modul.aksi` (`[Authorize(Policy = "sop.approve")]`) yang tidak terdaftar manual. Nama lain yang tidak terdaftar tetap gagal keras agar salah ketik cepat ketahuan. Tes refleksi memastikan semua policy di controller ada di katalog permission.
2. **Pencocokan pada scope mana pun.** `PermissionHandler` meloloskan user bila ia memegang permission itu pada scope apa pun (global, departemen, proyek). Pemeriksaan scope terhadap sumber daya tertentu tetap tugas service pemilik sumber daya (pemeriksaan kepemilikan objek, CONTEXT.md bagian 8).
3. **Fail closed.** Bila permission tidak dapat diambil (User Service atau database error), akses ditolak dan error dicatat. Permission kosong (user tanpa role) juga di-cache.
4. **Cache-aside di Redis**: key `tnt:{tenantId}:perm:{userId}`, TTL 15 menit. Key sengaja tanpa `Cache:KeyPrefix` per service (ADR-0007) agar penulis (User Service) dan pembaca (semua service) memakai key yang sama. Redis hanya cache: kegagalan baca dianggap miss, sehingga aplikasi tetap berjalan bila Redis mati.
5. **Sumber permission.** User Service membaca langsung dari database lokalnya. Service lain memanggil `GET /api/v1/users/{id}/permissions` dengan token user itu sendiri; endpoint ini hanya meminta login, dan membaca milik orang lain butuh `user.manage` (menghindari ketergantungan melingkar pada policy yang sedang dibangun).
6. **Invalidasi langsung, bukan consumer.** Selama belum ada messaging, `UserDbContext` menghapus key cache user terdampak segera setelah commit yang memuat `RoleChanged` (user langsung, atau semua pemegang role). Ini bergantung pada Redis bersama. Event tetap masuk outbox; consumer `RoleChanged` ditambahkan setelah NX-028 dan menggantikan invalidasi langsung ini.
7. **Best effort.** Kegagalan menghapus cache tidak membatalkan perubahan role yang sudah tersimpan; dicatat sebagai error, dan perubahan paling lambat berlaku setelah TTL. Key dihapus satu per satu (kompatibel dengan Redis cluster).
8. **Handler diambil lazy.** `PermissionHandler` baru meresolusi `IPermissionService` saat dibutuhkan, agar endpoint tanpa pemeriksaan permission (health) tidak membentuk koneksi database atau Redis.
9. **Scope global untuk endpoint kelola.** Policy `user.manage` dan `role.manage` di User Service mensyaratkan penugasan role global; role bersope departemen/proyek tidak memberi hak kelola seluruh tenant.
10. **User nonaktif tidak punya permission.** Perubahan status mengirim `RoleChanged` sehingga cache user itu dihapus, dan pembacaan permission mengembalikan daftar kosong untuk akun nonaktif.
11. **Kegagalan tidak di-cache.** Sumber yang tidak terjangkau (termasuk timeout dan 404) ditolak tanpa menyimpan apa pun ke cache; hanya pembatalan oleh klien yang diteruskan sebagai pembatalan.
12. **Bootstrap Development.** `DevSeeder` User Service membuat tenant `demo` (ID 1, tanpa DDL; identity tidak diubah), admin dev dengan `PublicId` yang sama seperti kredensial Auth, dan role `TENANT_ADMIN`; hanya berjalan di Development.

## Konsekuensi

- Perubahan role/permission berlaku seketika selama Redis bersama dan terjangkau; bila tidak, paling lambat 15 menit.
- Setiap cache miss di User Service menjalankan beberapa query ringan; optimasi query khusus permission dan memoization per request ditunda.
- Race cache-aside tetap mungkin (pembacaan lama menimpa key yang baru dihapus); dibatasi oleh TTL 15 menit.
- Retry dan circuit breaker (Polly) untuk panggilan ke User Service belum ada karena paketnya belum tersedia di repo; saat ini hanya timeout (`Services:User:TimeoutSeconds`, default 5 detik). Harus ditambahkan sebelum service lain memakai `UserServicePermissionSource` di produksi.
- Service selain User perlu `Services:User:BaseUrl` dan memanggil `AddNexusPermissionAuthorization` setelah `AddNexusJwtAuth`.
