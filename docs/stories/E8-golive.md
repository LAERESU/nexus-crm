# E8 · Audit, Observability & Go-live

Tujuan epic: sistem siap produksi — tercatat, terpantau, teruji beban dan keamanan, lalu dirilis.
Referensi utama: `ARCHITECTURE.md` C.6, F.6, G.5, G.6; kriteria siap produksi di `PLANNING.md` bagian 7.

---

### NX-059 · Audit Service
- **Sprint:** 9 · **SP:** 8 · **Bergantung pada:** NX-028
- **Langkah:**
  1. Migrasi `db/aud/V2__audit.sql`: `AUDIT_LOGS` berpartisi bulanan (DDL ARCHITECTURE C.6; jika lisensi Partitioning tidak ada, tabel biasa + job purge).
  2. `AuditSaveChangesInterceptor` di BuildingBlocks: tangkap perubahan entitas (nilai lama/baru, field sensitif di-mask) → publish `AuditEvent` via outbox.
  3. Audit Service mengonsumsi dan menyimpan; user database aplikasi hanya punya `INSERT` dan `SELECT`.
  4. Catat juga login/logout/gagal login, perubahan role, dan 403.
  5. Endpoint `GET /audit?entityType=&entityId=&actorId=&from=&to=` (permission `audit.read`) dan tab Aktivitas di detail proyek/tugas/SOP.
- **Acceptance criteria:**
  - [ ] Setiap perubahan proyek, tugas, role, dan siklus SOP muncul di audit log.
  - [ ] User aplikasi tidak bisa `UPDATE`/`DELETE` tabel audit.

---

### NX-060 · OpenTelemetry + dashboard Grafana
- **Sprint:** 9 · **SP:** 5 · **Bergantung pada:** NX-010
- **Langkah:** pasang tracing dan metrik (ARCHITECTURE F.6) di semua service, endpoint `/metrics`; stack Prometheus + Grafana + Loki + Tempo di docker-compose dan Helm (sesuai keputusan D5); dashboard RED per service, pool Oracle, hit ratio Redis, antrean RabbitMQ.
- **Acceptance criteria:**
  - [ ] Satu request bisa ditelusuri dari Gateway sampai query Oracle di Tempo/Jaeger.
  - [ ] Dashboard per service tersedia sebagai file JSON di repo.

---

### NX-061 · Alerting
- **Sprint:** 9 · **SP:** 3 · **Bergantung pada:** NX-060
- **Langkah:** aturan alert: 5xx > 2% selama 5 menit, latency p95 > 800 ms, pod restart > 3 dalam 10 menit, Redis/Oracle/RabbitMQ down, antrean outbox > 1.000; kanal ke Teams/Slack/email.
- **Acceptance criteria:**
  - [ ] Mematikan Redis di staging memicu alert dalam 2 menit.

---

### NX-062 · Load test & performa
- **Sprint:** 9 · **SP:** 5 · **Bergantung pada:** NX-060
- **Langkah:** skrip k6 di `tests/load/` untuk login, list proyek, board, ubah status, baca SOP; jalankan di staging dengan beban target (asumsi 500 user bersamaan); perbaiki query/index/cache yang menjadi bottleneck; catat hasil di `docs/perf/`.
- **Acceptance criteria:**
  - [ ] p95 < 800 ms dan error rate < 1% pada beban target.

---

### NX-063 · Security test & hardening
- **Sprint:** 10 · **SP:** 8 · **Bergantung pada:** NX-059
- **Langkah:** OWASP ZAP baseline/full scan di staging (bisa ditambahkan ke CI), checklist OWASP ASVS L2, review NetworkPolicy dan secret, aktifkan TDE/TLS sesuai keputusan D4, pentest oleh pihak independen bila tersedia; perbaiki temuan.
- **Acceptance criteria:**
  - [ ] Tidak ada temuan High/Critical yang terbuka.
  - [ ] Laporan hasil scan disimpan di `docs/security/`.

---

### NX-064 · UAT, runbook, rilis produksi
- **Sprint:** 10 · **SP:** 5 · **Bergantung pada:** NX-061, NX-062, NX-063
- **Langkah:**
  1. Skenario UAT di `docs/uat/` untuk alur proyek→tugas dan SOP draft→publish→acknowledge.
  2. Runbook `docs/runbook.md`: deploy, rollback, restore backup Oracle, rotasi secret, penanganan alert.
  3. Uji restore backup Oracle.
  4. PR rilis `develop` → `main`, tag `v1.0.0`, deploy production dengan approval, smoke test.
- **Acceptance criteria:**
  - [ ] Semua kriteria siap produksi di `PLANNING.md` bagian 7 tercentang.
  - [ ] UAT ditandatangani product owner.
  - [ ] `v1.0.0` berjalan di production.
