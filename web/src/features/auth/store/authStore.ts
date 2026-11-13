import { create } from 'zustand'

export interface SessionUser {
  id: string
  tenantId: string
  roles: string[]
}

/** Satu permission beserta scope penugasan role yang memberikannya (`global`, `department`, `project`). */
export interface PermissionGrant {
  code: string
  scopeType: string
}

/** `loading` sampai daftar permission sesi ini dimuat; `error` bila gagal dimuat dan belum ada daftar sebelumnya. */
export type PermissionsStatus = 'loading' | 'ready' | 'error'

/**
 * Permission yang hanya berlaku bila diberikan di scope global, sama dengan policy backend (User Service
 * `Policies.Register`): penugasan role berscope departemen/proyek tidak cukup.
 */
const GLOBAL_ONLY_PERMISSIONS = new Set(['user.manage', 'role.manage'])

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
  /** Permission user untuk tampilan saja (menu, route, tombol); otorisasi tetap di backend. */
  permissions: PermissionGrant[]
  permissionsStatus: PermissionsStatus
  /** Sesi baru dari login (generasi naik). */
  login: (accessToken: string) => void
  /** Token baru dari refresh untuk sesi yang sama (generasi tetap). */
  setAccessToken: (accessToken: string) => void
  clear: () => void
  /** Menyimpan permission hasil muat; diabaikan bila sesi atau user sudah berganti sejak pemuatan dimulai. */
  setPermissions: (generation: number, userId: string, permissions: PermissionGrant[]) => void
  /** Gagal memuat: daftar yang sudah ada (mis. dari sebelum refresh token) tetap dipakai. */
  failPermissions: (generation: number, userId: string) => void
  /** Pemuatan diulang setelah gagal: kembali ke `loading`. */
  retryPermissions: (generation: number) => void
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

const noPermissions = {
  permissions: [] as PermissionGrant[],
  permissionsStatus: 'loading' as const,
}

/** Apakah daftar permission memberi akses `permission` (lihat {@link GLOBAL_ONLY_PERMISSIONS}). */
export function grantsPermission(permissions: PermissionGrant[], permission: string): boolean {
  const globalOnly = GLOBAL_ONLY_PERMISSIONS.has(permission)
  return permissions.some((p) => p.code === permission && (!globalOnly || p.scopeType === 'global'))
}

export const useAuthStore = create<AuthState>()((set, get) => {
  const apply = (accessToken: string, nextGeneration: number) => {
    const user = decodeSessionUser(accessToken)
    // Sesi baru (generasi naik) atau user lain (cookie diganti tab lain) mulai tanpa permission; refresh token
    // untuk user dan sesi yang sama mempertahankannya.
    const sameSession =
      user !== null && nextGeneration === get().generation && get().user?.id === user.id
    set({
      ...(user
        ? { accessToken, user, status: 'authenticated' }
        : { accessToken: null, user: null, status: 'anonymous' }),
      generation: nextGeneration,
      ...(sameSession ? {} : noPermissions),
    })
  }

  const isCurrent = (generation: number, userId: string) =>
    get().generation === generation && get().user?.id === userId

  return {
    accessToken: null,
    user: null,
    status: 'unknown',
    generation: 0,
    ...noPermissions,
    login: (accessToken) => apply(accessToken, get().generation + 1),
    setAccessToken: (accessToken) => apply(accessToken, get().generation),
    clear: () =>
      set((s) => ({
        accessToken: null,
        user: null,
        status: 'anonymous',
        generation: s.generation + 1,
        ...noPermissions,
      })),
    setPermissions: (generation, userId, permissions) => {
      if (isCurrent(generation, userId)) set({ permissions, permissionsStatus: 'ready' })
    },
    failPermissions: (generation, userId) => {
      if (isCurrent(generation, userId) && get().permissionsStatus !== 'ready')
        set({ permissionsStatus: 'error' })
    },
    retryPermissions: (generation) => {
      if (get().generation === generation && get().permissionsStatus === 'error')
        set({ permissionsStatus: 'loading' })
    },
    hasRole: (role) => get().user?.roles.includes(role) ?? false,
  }
})
