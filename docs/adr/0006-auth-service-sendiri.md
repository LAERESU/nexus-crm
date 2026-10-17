# ADR-0006: Auth Service Sendiri dengan JWT RS256 (D2)

- **Status:** Diterima
- **Tanggal:** 2026-10-18

## Konteks

Keputusan terbuka D2 (`docs/PLANNING.md`) menanyakan penyedia identitas: Auth Service sendiri, Keycloak, atau Azure AD. Rincian story E1 (NX-012 s/d NX-017) sudah ditulis untuk Auth Service sendiri.

## Keputusan

1. **Auth Service sendiri** menerbitkan token; tidak memakai Keycloak/Azure AD.
2. **Token dibuat dengan `Microsoft.IdentityModel.JsonWebTokens`** (pustaka resmi Microsoft yang sama dengan validator JwtBearer), RS256, bukan OpenIddict. Kunci publik diterbitkan di `/.well-known/jwks.json`. Kebutuhan saat ini hanya login SPA; OpenIddict dipertimbangkan bila nanti ada klien OAuth pihak ketiga. Kriptografi tidak ditulis sendiri.
3. **Email unik secara global.** Body login cukup `{ email, password }`. Pencarian kredensial berdasarkan email saat login adalah **satu-satunya pengecualian** aturan "setiap query wajib memfilter `TENANT_ID`" (CONTEXT.md bagian 3 poin 8), karena tenant baru diketahui dari kredensial tersebut. Tenant lalu dimasukkan ke klaim `tid`, dan semua query lain tetap memfilter `TENANT_ID`.
4. **Klaim `roles` lewat port `IUserDirectory`.** Sampai User Service tersedia (NX-018/NX-020), implementasi sementara membaca role dari kolom `CREDENTIALS.ROLES` (data seed dev). Kolom itu dihapus lewat migrasi baru saat implementasi diganti panggilan ke User Service dengan cache.
5. Password di-hash dengan **BCrypt** (work factor 12). Refresh token acak 256 bit, hanya hash SHA-256-nya yang disimpan.

## Konsekuensi

- Email tidak bisa dipakai di dua tenant sekaligus.
- Kunci privat RS256 wajib disediakan dari secret di luar Development; di Development dibuat kunci sementara saat start (token tidak berlaku lagi setelah restart).
- Nama klaim yang dipakai seluruh platform: `sub` (user), `tid` (tenant), `jti`, `roles`.
