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

  logout: () =>
    httpClient
      .post<void>('/auth/logout', undefined, { skipAuthRefresh: true })
      .then(() => undefined),
}
