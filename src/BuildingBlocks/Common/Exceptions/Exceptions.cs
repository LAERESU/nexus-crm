namespace NexUS.BuildingBlocks.Common.Exceptions;

/// <summary>Pelanggaran aturan bisnis (HTTP 422).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Data tidak ditemukan (HTTP 404).</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Akses ditolak, termasuk kepemilikan objek (HTTP 403).</summary>
public class ForbiddenException(string message) : Exception(message);

/// <summary>Konflik konkurensi atau ROW_VERSION tidak cocok (HTTP 409).</summary>
public class ConcurrencyException(string message) : Exception(message);

/// <summary>Autentikasi gagal (HTTP 401). Pesan harus generik agar tidak membocorkan detail.</summary>
public class UnauthorizedException(string message) : Exception(message);
