import { create } from 'zustand'

export interface SessionUser {
  id: string
  name: string
  tenantId: string
}

interface AuthState {
  /** Disimpan di memori, bukan localStorage (CONTEXT.md bagian 7). */
  accessToken: string | null
  user: SessionUser | null
  permissions: string[]
  isAuthenticated: boolean
  setSession: (token: string, user: SessionUser, permissions: string[]) => void
  clear: () => void
  hasPermission: (permission: string) => boolean
}

export const useAuthStore = create<AuthState>()((set, get) => ({
  accessToken: null,
  user: null,
  permissions: [],
  isAuthenticated: false,
  setSession: (accessToken, user, permissions) =>
    set({ accessToken, user, permissions, isAuthenticated: true }),
  clear: () => set({ accessToken: null, user: null, permissions: [], isAuthenticated: false }),
  hasPermission: (permission) => get().permissions.includes(permission),
}))
