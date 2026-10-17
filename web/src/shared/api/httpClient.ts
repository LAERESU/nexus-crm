import axios from 'axios'
import { useAuthStore } from '@/features/auth/store/authStore'

/**
 * Satu-satunya pintu HTTP aplikasi; komponen tidak boleh memanggil axios/fetch langsung.
 * Interceptor refresh token dilengkapi di NX-017.
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
