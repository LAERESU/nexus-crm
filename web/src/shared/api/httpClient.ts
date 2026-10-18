import axios, { AxiosError } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
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

interface TokenResponse {
  accessToken: string
}

/**
 * Satu-satunya pintu HTTP aplikasi; komponen tidak boleh memanggil axios/fetch langsung.
 * Access token disisipkan sebagai Bearer; saat 401, refresh dilakukan **satu kali** untuk semua request yang
 * sedang gagal (single-flight, syarat rotasi refresh token NX-013), lalu request diulang sekali. Bila refresh gagal,
 * sesi dibersihkan dan router mengarahkan ke /login.
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

let refreshInFlight: Promise<string> | null = null

/** Menukar refresh token (cookie) dengan access token baru; panggilan bersamaan berbagi satu request. */
export function refreshAccessToken(): Promise<string> {
  refreshInFlight ??= httpClient
    .post<TokenResponse>('/auth/refresh', undefined, { skipAuthRefresh: true })
    .then((response) => {
      useAuthStore.getState().setSession(response.data.accessToken)
      return response.data.accessToken
    })
    .catch((error: unknown) => {
      useAuthStore.getState().clear()
      throw error
    })
    .finally(() => {
      refreshInFlight = null
    })
  return refreshInFlight
}

httpClient.interceptors.response.use(undefined, async (error: unknown) => {
  if (!(error instanceof AxiosError) || error.response?.status !== 401 || !error.config) throw error

  const original: InternalAxiosRequestConfig = error.config
  if (original.skipAuthRefresh || original.retriedAfterRefresh) throw error

  const token = await refreshAccessToken() // gagal → sesi dibersihkan, error refresh diteruskan
  original.retriedAfterRefresh = true
  original.headers.set('Authorization', `Bearer ${token}`)
  return httpClient(original)
})
