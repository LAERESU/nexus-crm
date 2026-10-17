# ADR-0002: Memakai .NET 10 LTS

- **Status:** Diterima
- **Tanggal:** 2026-10-17

## Konteks

Dokumen awal menetapkan .NET 8 LTS. Mesin pengembangan sudah memakai SDK .NET 10 dan tidak memiliki runtime .NET 8. .NET 10 adalah rilis LTS yang lebih baru dengan masa dukungan lebih panjang.

## Keputusan

Seluruh backend memakai **.NET 10 LTS** (`net10.0`, SDK dikunci lewat `global.json`). Image Docker memakai `mcr.microsoft.com/dotnet/sdk:10.0` dan `aspnet:10.0`, dan CI memakai setup .NET 10. Aturan di `docs/CONTEXT.md` bagian 4 diperbarui.

## Konsekuensi

- Versi paket NuGet di `Directory.Packages.props` harus kompatibel dengan `net10.0`.
- Semua anggota tim memasang SDK .NET 10.
- Penomoran ADR template service di NX-003 bergeser menjadi `0003`.
