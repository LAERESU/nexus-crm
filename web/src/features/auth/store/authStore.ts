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
  /**
   * Naik setiap login dan logout. Refresh yang dimulai pada generasi lama hasilnya diabaikan, agar refresh yang
   * selesai terlambat tidak menimpa sesi yang lebih baru (mis. login baru atau logout).
   */
  generation: number
  /** Sesi baru dari login (generasi naik). */
  login: (accessToken: string) => void
  /** Token baru dari refresh untuk sesi yang sama (generasi tetap). */
  setAccessToken: (accessToken: string) => void
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

export const useAuthStore = create<AuthState>()((set, get) => {
  const apply = (accessToken: string, nextGeneration: number) => {
    const user = decodeSessionUser(accessToken)
    set(
      user
        ? { accessToken, user, status: 'authenticated', generation: nextGeneration }
        : { accessToken: null, user: null, status: 'anonymous', generation: nextGeneration },
    )
  }

  return {
    accessToken: null,
    user: null,
    status: 'unknown',
    generation: 0,
    login: (accessToken) => apply(accessToken, get().generation + 1),
    setAccessToken: (accessToken) => apply(accessToken, get().generation),
    clear: () =>
      set((s) => ({
        accessToken: null,
        user: null,
        status: 'anonymous',
        generation: s.generation + 1,
      })),
    hasRole: (role) => get().user?.roles.includes(role) ?? false,
  }
})
