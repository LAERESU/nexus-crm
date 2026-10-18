import { create } from 'zustand'

export interface SessionUser {
  id: string
  tenantId: string
  roles: string[]
}

/** `unknown` saat aplikasi baru dimuat dan sesi belum dipulihkan lewat refresh token. */
export type AuthStatus = 'unknown' | 'authenticated' | 'anonymous'

interface AuthState {
  /** Disimpan di memori, bukan localStorage/sessionStorage (CONTEXT.md bagian 7). */
  accessToken: string | null
  user: SessionUser | null
  status: AuthStatus
  isAuthenticated: boolean
  setSession: (accessToken: string) => void
  clear: () => void
  hasRole: (role: string) => boolean
}

/**
 * Membaca klaim dari payload JWT untuk tampilan saja (tanpa verifikasi tanda tangan); otorisasi tetap di backend.
 * Mengembalikan null bila token rusak.
 */
export function decodeSessionUser(accessToken: string): SessionUser | null {
  try {
    const payload = accessToken.split('.')[1]
    if (!payload) return null
    const json = atob(
      payload
        .replace(/-/g, '+')
        .replace(/_/g, '/')
        .padEnd(Math.ceil(payload.length / 4) * 4, '='),
    )
    const claims = JSON.parse(json) as { sub?: unknown; tid?: unknown; roles?: unknown }
    if (typeof claims.sub !== 'string' || typeof claims.tid !== 'string') return null

    const roles = Array.isArray(claims.roles)
      ? claims.roles.filter((r): r is string => typeof r === 'string')
      : typeof claims.roles === 'string'
        ? [claims.roles]
        : []
    return { id: claims.sub, tenantId: claims.tid, roles }
  } catch {
    return null
  }
}

export const useAuthStore = create<AuthState>()((set, get) => ({
  accessToken: null,
  user: null,
  status: 'unknown',
  isAuthenticated: false,
  setSession: (accessToken) => {
    const user = decodeSessionUser(accessToken)
    if (!user) {
      set({ accessToken: null, user: null, status: 'anonymous', isAuthenticated: false })
      return
    }
    set({ accessToken, user, status: 'authenticated', isAuthenticated: true })
  },
  clear: () => set({ accessToken: null, user: null, status: 'anonymous', isAuthenticated: false }),
  hasRole: (role) => get().user?.roles.includes(role) ?? false,
}))
