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
   - mengubah/mengarsipkan proyek dan mengelola anggota: permission-nya harus mencakup proyek (scope global, proyek itu, atau departemennya) **dan** user adalah `MANAGER` proyek itu atau pemegang `project.all.read`.
3. **Manager terakhir** tidak bisa dikeluarkan atau diturunkan (aturan Domain). Setiap perubahan anggota menaikkan `ROW_VERSION` proyek, sehingga dua perubahan anggota yang bersamaan tidak bisa sama-sama lolos (yang kedua 409).
4. **`USER_REF`** diisi consumer `project.UserCreated`, `project.UserUpdated`, dan `project.UserDeactivated`, idempotent lewat `PROCESSED_EVENTS`. Ketiga antrean diproses terpisah, jadi event bisa tiba tidak berurutan: perubahan dari event yang lebih lama daripada `LAST_EVENT_AT` diabaikan.
5. **User Service kini mempublikasikan `UserUpdated` (memuat `IsActive`) dan `UserDeactivated`** selain `UserCreated`.
6. **User yang belum dikenal `USER_REF` tetap boleh dijadikan anggota** (nama tampil kosong sampai event-nya tiba); yang diketahui nonaktif ditolak. Tidak ada panggilan sinkron ke User Service untuk memvalidasi anggota.

## Konsekuensi

- User yang dibuat sebelum Project Service mulai mengonsumsi event tidak ada di `USER_REF` sampai profilnya berubah; namanya tampil kosong di daftar anggota. Belum ada mekanisme isi ulang (backfill).
- Karena publish bersifat `mandatory` (ADR-0010), antrean `project.UserUpdated`/`project.UserDeactivated` harus sudah ada sebelum User Service mengirim event itu; sampai Project Service pernah berjalan, relay User Service tertahan pada event tersebut (termasuk `UserCreated` di belakangnya).
- User yang dinonaktifkan tetap tercatat sebagai anggota proyek; hanya penambahannya ke proyek lain yang ditolak.
- Pemegang permission berscope proyek/departemen tetap harus dijadikan manager proyek untuk bisa mengubahnya.
- Daftar anggota tidak dipaging (jumlah anggota satu proyek dianggap terbatas).
