# ADR-0011: Akses Proyek Berdasarkan Keanggotaan dan Read Model User

- **Status:** Diterima
- **Tanggal:** 2026-11-14

## Konteks

NX-026 meminta: hanya anggota proyek yang boleh membaca proyek (kecuali admin/auditor), hanya manager proyek atau admin tenant yang boleh mengelola anggota, dan Project Service menyimpan read model user (`USER_REF`) dari event User Service.

Permission bawaan tidak cukup untuk membedakan "anggota" dari "admin/auditor": role `MEMBER` memegang `project.read` secara global, sehingga `project.read` global tidak bisa berarti "boleh melihat semua proyek". Role `PROJECT_MANAGER` juga memegang `project.member.manage` secara global, sehingga permission itu saja tidak bisa berarti "manager proyek ini".

## Keputusan

1. **Permission baru `project.all.read`** ("melihat semua proyek tanpa menjadi anggota"), diberikan ke `TENANT_ADMIN` dan `AUDITOR`. `TENANT_ADMIN` juga mendapat `project.member.manage`.
2. **Kepemilikan objek diperiksa di use case (`ProjectGuard`)**, setelah policy permission di endpoint:
   - membaca proyek dan anggotanya: anggota proyek (peran apa pun) atau pemegang `project.all.read`;
   - daftar proyek: hanya proyek yang diikuti, kecuali pemegang `project.all.read`;
   - mengubah/mengarsipkan proyek dan mengelola anggota: permission-nya harus mencakup proyek (scope global, proyek itu, atau departemennya) **dan** user adalah `MANAGER` proyek itu. Pengecualian hanya bagi pemegang `project.all.read` yang juga memegang permission tulis itu secara **global** (admin tenant); izin baca saja tidak membuka operasi tulis.
3. **Manager terakhir** tidak bisa dikeluarkan atau diturunkan (aturan Domain). Setiap perubahan anggota menandai aggregate berubah (`AggregateRoot.MarkChildrenChanged`) sehingga `ROW_VERSION` proyek naik, dan dua perubahan anggota yang bersamaan tidak bisa sama-sama lolos (yang kedua 409). Penambahan user yang sama secara bersamaan dijawab 422.
4. **`USER_REF`** diisi consumer `project.UserCreated`, `project.UserUpdated`, dan `project.UserDeactivated`, idempotent lewat `PROCESSED_EVENTS`. Ketiga antrean diproses terpisah, jadi event bisa tiba tidak berurutan: profil dan status aktif masing-masing punya waktu event terakhir (`PROFILE_EVENT_AT`, `STATUS_EVENT_AT`), dan perubahan dari event yang lebih lama diabaikan tanpa saling menghalangi.
5. **User Service kini mempublikasikan `UserUpdated` (memuat `IsActive`) dan `UserDeactivated`** selain `UserCreated`. `UserUpdated` yang sudah ada di outbox sebelum perubahan ini tidak memuat status; `IsActive`-nya null dan consumer tidak mengubah status (agar user nonaktif tidak aktif kembali).
6. **User yang belum dikenal `USER_REF` tetap boleh dijadikan anggota** (nama tampil kosong sampai event-nya tiba); yang diketahui nonaktif ditolak. Tidak ada panggilan sinkron ke User Service untuk memvalidasi anggota.

## Konsekuensi

- User yang dibuat sebelum Project Service mulai mengonsumsi event tidak ada di `USER_REF` sampai profilnya berubah; namanya tampil kosong di daftar anggota. Belum ada mekanisme isi ulang (backfill).
- Karena publish bersifat `mandatory` (ADR-0010), antrean `project.UserUpdated`/`project.UserDeactivated` harus sudah ada sebelum User Service mengirim event itu; sampai Project Service pernah berjalan, relay User Service tertahan pada event tersebut (termasuk `UserCreated` di belakangnya).
- User yang dinonaktifkan tetap tercatat sebagai anggota proyek; hanya penambahannya ke proyek lain yang ditolak.
- Pemegang permission berscope proyek/departemen tetap harus dijadikan manager proyek untuk bisa mengubahnya.
- Daftar anggota memakai paging seperti endpoint list lain.
- Pemetaan `PROCESSED_EVENTS` kini ada di BuildingBlocks (`MapProcessedEvents`) dan dipakai Auth serta Project.
