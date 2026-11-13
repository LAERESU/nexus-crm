import type { PermissionGrant } from '@/features/auth/store/authStore'
import { httpClient, refreshAccessToken } from '@/shared/api/httpClient'

const PERMISSIONS_TIMEOUT_MS = 10_000

const isPermissionGrant = (value: unknown): value is PermissionGrant =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as PermissionGrant).code === 'string' &&
  typeof (value as PermissionGrant).scopeType === 'string'

export interface TokenResponse {
  accessToken: string
  tokenType: string
  expiresIn: number
}

export interface LoginRequest {
  email: string
  password: string
}

/** Endpoint auth tidak memicu refresh otomatis (lihat interceptor di httpClient). */
export const authApi = {
  login: (request: LoginRequest) =>
    httpClient
      .post<TokenResponse>('/auth/login', request, { skipAuthRefresh: true })
      .then((r) => r.data),

  /** Mengisi password pertama akun undangan dengan token dari email (NX-021). */
  activate: (token: string, password: string) =>
    httpClient
      .post<void>('/auth/activate', { token, password }, { skipAuthRefresh: true })
      .then(() => undefined),

  /** Memulihkan sesi dari refresh token (cookie httpOnly); berbagi satu request dengan refresh otomatis. */
  restoreSession: () => refreshAccessToken(),

  /**
   * Akses efektif user yang sedang login (`GET /users/me/permissions`). Respons yang bentuknya tidak dikenal
   * dianggap gagal, bukan "tanpa permission"; request dibatasi waktu agar layar tunggu tidak menggantung.
   */
  permissions: () =>
    httpClient
      .get<{ permissions?: unknown }>('/users/me/permissions', { timeout: PERMISSIONS_TIMEOUT_MS })
      .then((r) => {
        const permissions: unknown = r.data?.permissions
        if (!Array.isArray(permissions) || !permissions.every(isPermissionGrant))
          throw new Error('Respons permission tidak dikenal.')
        return permissions.map(({ code, scopeType }) => ({ code, scopeType }))
      }),

  logout: () =>
    httpClient
      .post<void>('/auth/logout', undefined, { skipAuthRefresh: true })
      .then(() => undefined),
}
