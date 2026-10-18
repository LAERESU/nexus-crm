import { AxiosError } from 'axios'

/** Pesan untuk pengguna; pesan 401 sengaja generik, sama dengan backend. */
export function loginErrorMessage(error: unknown): string {
  if (error instanceof AxiosError) {
    if (error.response?.status === 401) return 'Email atau password salah.'
    if (error.response?.status === 429) {
      const seconds = Number(error.response.headers['retry-after'])
      return Number.isFinite(seconds) && seconds > 0
        ? `Terlalu banyak percobaan. Coba lagi dalam ${Math.ceil(seconds / 60)} menit.`
        : 'Terlalu banyak percobaan. Coba lagi nanti.'
    }
    if (error.response?.status === 400) return 'Periksa kembali email dan password.'
  }
  return 'Tidak dapat terhubung ke server. Coba lagi.'
}
