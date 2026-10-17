# ADR-0004: Stack Frontend (React 18, Ant Design, Vite)

- **Status:** Diterima
- **Tanggal:** 2026-10-17

## Konteks

NX-004 meminta pilihan pustaka UI (Ant Design atau MUI) dicatat. `docs/CONTEXT.md` menetapkan React 18 + TypeScript, sedangkan template Vite terbaru memasang React 19.

## Keputusan

- **React 18** (dipaku `react@18`) sesuai CONTEXT.md; Vite 8 sebagai bundler, TypeScript `strict`.
- **Ant Design** untuk komponen: tabel, form, date picker, dan layout enterprise sudah tersedia sehingga mengurangi kode kustom.
- **ESLint** (typescript-eslint, aturan `no-explicit-any` sebagai error) + **Prettier**, menggantikan oxlint bawaan template.
- **Vitest** + Testing Library; alias `@/` mengarah ke `src/`.
- Menu sidebar selain Dashboard dinonaktifkan sampai halamannya dibuat di story masing-masing.

## Konsekuensi

- Naik ke React 19 membutuhkan ADR baru dan pemeriksaan kompatibilitas pustaka.
- Bundle awal memuat Ant Design (~120 kB gzip); halaman dimuat lazy per route.
