# E7 · Notification

Tujuan epic: user mendapat notifikasi real-time dan email untuk kejadian penting.
Referensi utama: `ARCHITECTURE.md` B.6.

---

### NX-055 · Consumer event + simpan notifikasi
- **Sprint:** 6 · **SP:** 5 · **Bergantung pada:** NX-028
- **Langkah:**
  1. Migrasi `db/ntf/V2__notifications.sql`: `NOTIFICATIONS`, `NOTIFICATION_TEMPLATES`, `NOTIFICATION_PREFERENCES`, `DELIVERY_LOGS`.
  2. Consumer untuk `TaskAssigned`, `TaskStatusChanged`, `TaskCommented`, `MemberAdded`, `SopSubmitted`, `SopApproved`, `SopRejected`, `SopPublished`, `UserCreated`.
  3. Idempotency key `ntf:evt:{eventId}` (24 jam) + tabel `PROCESSED_EVENTS`.
  4. Endpoint list, `unread-count` (cache `ntf:unread:{userId}`), tandai dibaca, tandai semua dibaca.
- **Acceptance criteria:**
  - [ ] Event yang terkirim dua kali hanya menghasilkan satu notifikasi.
  - [ ] User tidak menerima notifikasi untuk aksinya sendiri.

---

### NX-056 · Push real-time SignalR + bell
- **Sprint:** 6 · **SP:** 5 · **Bergantung pada:** NX-055, NX-011
- **Langkah:** hub `/hubs/notifications` dengan autentikasi JWT (token via query string untuk WebSocket), backplane Redis; frontend `useNotificationHub` dan `NotificationBell` dengan badge dan dropdown.
- **Acceptance criteria:**
  - [ ] Notifikasi muncul < 2 detik setelah tugas ditugaskan, tanpa reload.
  - [ ] Berfungsi dengan 2 replika Notification Service.

---

### NX-057 · Email notifikasi
- **Sprint:** 7 · **SP:** 5 · **Bergantung pada:** NX-055
- **Langkah:** template email (Razor/Scriban) per jenis event, antrean `EMAIL_QUEUE` dengan retry eksponensial (maks 5 kali), SMTP dari konfigurasi (Mailpit untuk dev); pindahkan email undangan NX-021 ke sini.
- **Acceptance criteria:**
  - [ ] SMTP mati sementara → email terkirim setelah pulih tanpa duplikat.
  - [ ] Setiap pengiriman tercatat di `DELIVERY_LOGS`.

---

### NX-058 · Preferensi notifikasi
- **Sprint:** 8 · **SP:** 3 · **Bergantung pada:** NX-057
- **Langkah:** endpoint `GET/PUT /notifications/preferences` (per jenis event × kanal in-app/email); halaman pengaturan di profil user.
- **Acceptance criteria:**
  - [ ] Menonaktifkan email untuk "tugas ditugaskan" menghentikan email itu tetapi notifikasi in-app tetap ada.
