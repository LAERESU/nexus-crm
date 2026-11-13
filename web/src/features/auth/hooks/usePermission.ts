import { grantsPermission, useAuthStore } from '@/features/auth/store/authStore'

/** Apakah user yang login memegang `permission`. Untuk tampilan saja; otorisasi tetap di backend. */
export function usePermission(permission: string): boolean {
  return useAuthStore((s) => grantsPermission(s.permissions, permission))
}
