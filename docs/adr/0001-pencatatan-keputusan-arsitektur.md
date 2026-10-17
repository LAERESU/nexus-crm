# ADR-0001: Mencatat Keputusan Arsitektur dengan ADR

- **Status:** Diterima
- **Tanggal:** 2026-10-17

## Konteks

NexUS memakai banyak teknologi dan service. Alasan di balik sebuah keputusan mudah hilang bila hanya dibahas lisan atau di chat.

## Keputusan

Setiap keputusan arsitektur yang berdampak lintas service, mengubah aturan di `docs/CONTEXT.md`, atau menjawab keputusan terbuka D1–D6 di `docs/PLANNING.md` dicatat sebagai file `docs/adr/NNNN-judul.md` memakai `0000-template.md`.

## Konsekuensi

- Anggota tim baru bisa memahami alasan desain tanpa bertanya ulang.
- ADR yang diganti tidak dihapus; statusnya diubah menjadi "Digantikan oleh ADR-XXXX".
