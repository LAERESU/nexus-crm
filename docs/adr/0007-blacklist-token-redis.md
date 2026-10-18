# ADR-0007: Blacklist Access Token di Redis (per token dan per sesi)

- **Status:** Diterima
- **Tanggal:** 2026-10-18

## Konteks

NX-014 meminta logout yang membuat access token lama ditolak walau belum kedaluwarsa. ARCHITECTURE.md G.1 mencontohkan key Redis `auth:revoked:{jti}`. CONTEXT.md mewajibkan awalan `tnt:{tenantId}:` untuk setiap cache key (bagian 3 poin 8) dan menyatakan Redis hanya cache, aplikasi tetap berjalan bila Redis mati (bagian 3 poin 7).

Review menemukan tiga celah pada desain awal: blacklist kedaluwarsa tepat di `exp` padahal validator masih menerima token sampai `exp + ClockSkew`; access token lain dari sesi yang sama (mis. sebelum refresh terakhir) tetap berlaku; dan `Cache:KeyPrefix` per service bisa membuat penulis dan pembaca memakai key berbeda.

## Keputusan

1. Access token membawa klaim **`sid`** = ID keluarga refresh token (satu sesi login).
2. Dua jenis entri Redis, keduanya berawalan tenant dan **tanpa** `Cache:KeyPrefix`:
   - `tnt:{tid}:auth:revoked:{jti}`: satu token, TTL `exp + ClockSkew - now`.
   - `tnt:{tid}:auth:revoked-sid:{sid}`: seluruh sesi, TTL `AccessTokenLifetime + ClockSkew` (umur maksimal token sesi itu).
3. Logout mencabut sesi (`sid`) dan token di header. Deteksi pemakaian ulang refresh token (NX-013) juga mencabut sesi.
4. `ClockSkew` (30 detik) dan umur access token (15 menit) didefinisikan sekali di `NexusJwt` dan wajib dipakai validator (NX-015).
5. **Fail open:** bila Redis tidak terjangkau, token dianggap belum dicabut dan kejadiannya dicatat. Risikonya terbatas pada sisa umur access token (maksimal 15 menit); refresh token tetap dicabut di Oracle sebagai sumber kebenaran.
6. Tanpa `ConnectionStrings:Redis`, Development tetap berjalan tanpa cache, tetapi **di environment lain aplikasi gagal start**, supaya blacklist tidak diam-diam mati akibat konfigurasi yang terlewat.

## Konsekuensi

- Setiap validasi JWT di Gateway/service melakukan satu `EXISTS` ke Redis untuk dua key.
- Fail closed (menolak semua token saat Redis mati) sengaja tidak dipilih karena akan membuat seluruh API mati bersama Redis, bertentangan dengan CONTEXT.md bagian 3 poin 7.
