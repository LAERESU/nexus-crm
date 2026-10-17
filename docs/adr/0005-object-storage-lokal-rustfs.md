# ADR-0005: RustFS sebagai Object Storage Lokal

- **Status:** Diterima (khusus lingkungan lokal; keputusan D6 untuk produksi tetap terbuka)
- **Tanggal:** 2026-10-17

## Konteks

NX-005 meminta MinIO sebagai object storage di docker-compose. Image resmi MinIO tidak lagi dapat diunduh publik: `minio/minio` di Docker Hub kosong dan `quay.io/minio/minio` meminta autentikasi.

## Keputusan

Compose lokal memakai **RustFS** (`rustfs/rustfs:1.0.2`, Apache-2.0): kompatibel dengan S3 API, memiliki web console, dan image publik. API S3 di port 9000, console di 9001.

Karena kompatibel S3, kode aplikasi memakai klien S3 standar sehingga bisa berpindah ke S3, Azure Blob (lewat adaptor), atau MinIO berlisensi tanpa perubahan besar.

## Konsekuensi

- Pilihan object storage produksi (D6 di `docs/PLANNING.md`) tetap harus diputuskan sebelum story lampiran (E4).
- Tag image dipaku (`1.0.2`) agar lingkungan lokal reproducible.
