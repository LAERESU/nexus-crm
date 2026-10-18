import axios, { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import type { TokenResponse } from '@/features/auth/api/authApi'
import { useAuthStore } from '@/features/auth/store/authStore'

declare module 'axios' {
  interface AxiosRequestConfig {
    /** Jangan coba refresh token saat 401 (dipakai endpoint auth sendiri). */
    skipAuthRefresh?: boolean
  }
  interface InternalAxiosRequestConfig {
    /** Request ini sudah diulang sekali setelah refresh. */
    retriedAfterRefresh?: boolean
  }
}

/**
 * Satu-satunya pintu HTTP aplikasi; komponen tidak boleh memanggil axios/fetch langsung.
 * Access token disisipkan sebagai Bearer; saat 401, refresh dilakukan **satu kali** untuk semua request yang
 * sedang gagal, lalu request diulang sekali (lihat {@link refreshAccessToken}).
 */
export const httpClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api/v1',
  withCredentials: true, // refresh token ada di cookie httpOnly
})

httpClient.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken
  if (token) config.headers.set('Authorization', `Bearer ${token}`)
  config.headers.set('X-Correlation-Id', crypto.randomUUID())
  return config
})

/** Refresh yang sedang berjalan, dipakai bersama hanya oleh pemanggil dari generasi sesi yang sama. */
let refreshInFlight: { generation: number; promise: Promise<string> } | null = null

/** Menjalankan fungsi di bawah kunci lintas tab (Web Locks) bila tersedia, agar refresh berjalan satu per satu. */
function withCrossTabLock<T>(action: () => Promise<T>): Promise<T> {
  const locks = typeof navigator !== 'undefined' ? navigator.locks : undefined
  return locks ? locks.request('nexus-auth-refresh', action) : action()
}

/**
 * Menukar refresh token (cookie) dengan access token baru.
 * - Panggilan bersamaan di tab ini berbagi satu request; antar tab diserialkan dengan Web Locks, karena rotasi
 *   refresh token (NX-013) menolak refresh bersamaan. Cookie dipakai bersama semua tab, sehingga tab yang menunggu
 *   kunci memakai cookie terbaru.
 * - Hasilnya diabaikan bila sesi sudah berganti (login baru atau logout) selama refresh berjalan.
 * - Hanya 401 yang mengakhiri sesi; error lain (jaringan, 429, 5xx) diteruskan tanpa mengeluarkan user.
 */
export function refreshAccessToken(): Promise<string> {
  const startedAt = useAuthStore.getState().generation

  if (refreshInFlight?.generation === startedAt) return refreshInFlight.promise

  const promise: Promise<string> = withCrossTabLock(() =>
    httpClient.post<TokenResponse>('/auth/refresh', undefined, { skipAuthRefresh: true }),
  )
    .then((response) => {
      const store = useAuthStore.getState()
      if (store.generation === startedAt) store.setAccessToken(response.data.accessToken)
      return response.data.accessToken
    })
    .catch((error: unknown) => {
      const store = useAuthStore.getState()
      if (
        store.generation === startedAt &&
        error instanceof AxiosError &&
        error.response?.status === 401
      )
        store.clear()
      throw error
    })
    .finally(() => {
      if (refreshInFlight?.promise === promise) refreshInFlight = null
    })
  refreshInFlight = { generation: startedAt, promise }
  return promise
}

httpClient.interceptors.response.use(undefined, async (error: unknown) => {
  if (!(error instanceof AxiosError) || error.response?.status !== 401 || !error.config) throw error

  const original: InternalAxiosRequestConfig = error.config
  if (original.skipAuthRefresh || original.retriedAfterRefresh) throw error

  // Request yang berangkat dengan token lama padahal token sudah diperbarui request lain: cukup ulang dengan token
  // terbaru, tanpa refresh kedua (setiap refresh merotasi token dan memakai kuota rate limit).
  const current = useAuthStore.getState().accessToken
  const sentWith = AxiosHeaders.from(original.headers).get('Authorization')?.toString()
  const token = current && sentWith !== `Bearer ${current}` ? current : await refreshAccessToken()

  original.retriedAfterRefresh = true
  original.headers.set('Authorization', `Bearer ${token}`)
  return httpClient(original)
})
