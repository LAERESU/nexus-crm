import type { PermissionGrant } from '@/features/auth/store/authStore'
import { httpClient, refreshAccessToken } from '@/shared/api/httpClient'

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

  /** Memulihkan sesi dari refresh token (cookie httpOnly); berbagi satu request dengan refresh otomatis. */
  restoreSession: () => refreshAccessToken(),

  /** Akses efektif user sendiri dari User Service (`GET /users/{id}/permissions`). */
  permissions: (userId: string) =>
    httpClient
      .get<{ permissions?: unknown }>(`/users/${encodeURIComponent(userId)}/permissions`)
      .then((r) =>
        (Array.isArray(r.data?.permissions) ? r.data.permissions : []).filter(
          (p): p is PermissionGrant =>
            typeof p === 'object' &&
            p !== null &&
            typeof (p as PermissionGrant).code === 'string' &&
            typeof (p as PermissionGrant).scopeType === 'string',
        ),
      ),

  logout: () =>
    httpClient
      .post<void>('/auth/logout', undefined, { skipAuthRefresh: true })
      .then(() => undefined),
}
