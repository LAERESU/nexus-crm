# ADR-0008: Rate Limiting Gateway dengan Skrip Lua di Redis

- **Status:** Diterima
- **Tanggal:** 2026-10-18

## Konteks

NX-016 meminta rate limiting di Gateway (ARCHITECTURE.md G.4) yang hitungannya dibagi semua replika. ARCHITECTURE.md menyebut paket pihak ketiga `RedisRateLimiting` sebagai contoh. CONTEXT.md mewajibkan awalan `tnt:{tenantId}:` untuk setiap cache key (bagian 3 poin 8) dan Redis hanya sebagai cache (bagian 3 poin 7).

## Keputusan

1. **Limiter ditulis sendiri** sebagai satu skrip Lua atomik per request, bukan paket pihak ketiga, agar tidak menambah dependency keamanan dari pihak ketiga. Skrip memakai jam Redis (`TIME`) supaya replika dengan jam berbeda tetap konsisten.
2. Semua kebijakan untuk satu request dievaluasi dalam skrip yang sama; **kuota hanya dipakai bila semua kebijakan lolos**, sehingga request yang ditolak satu kebijakan (mis. per-user) tidak menguras kebijakan lain (mis. per-tenant).
3. Algoritma: fixed window (`login`, `upload`), **sliding log** eksak berbasis sorted set (`per-user`, `per-ip`, menjamin batas per 60 detik bergulir), token bucket (`per-tenant`).
4. **Pengecualian awalan tenant:** key `rl:login:{hash}` dan `rl:ip:{hash}` bersifat global karena tenant belum diketahui sebelum login. Key ini adalah penghitung sementara, bukan cache data tenant; IP dan email di-hash. Key per user/tenant tetap `tnt:{tid}:rl:...`.
5. Request anonim (tanpa token atau token tidak valid) dibatasi per IP sebagai pengganti per-user.
6. **Fail open:** bila Redis tidak terjangkau, request diizinkan, dicatat, dan pemeriksaan dilewati 30 detik (circuit breaker) agar request tidak menunggu timeout berulang kali.

## Konsekuensi

- Satu `EVAL` per request di Gateway (ditambah satu `EXISTS` untuk blacklist token).
- Sliding log menyimpan satu entri per request dalam window (maks 300 per user per menit).
- Di belakang ingress, Gateway harus memercayai forwarded headers dari ingress (NX-008) agar kunci IP benar.
