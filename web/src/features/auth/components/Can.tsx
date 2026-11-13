import type { ReactNode } from 'react'
import { usePermission } from '@/features/auth/hooks/usePermission'
import { useAuthStore } from '@/features/auth/store/authStore'

interface CanProps {
  permission: string
  children: ReactNode
  /** Ditampilkan bila user tidak memegang permission (bawaan: tidak ada). */
  fallback?: ReactNode
}

/** Menyembunyikan aksi yang tidak boleh dipakai user (tampilan saja; otorisasi tetap di backend); selama permission belum dimuat, aksi tersembunyi. */
export function Can({ permission, children, fallback = null }: CanProps) {
  const allowed = usePermission(permission)
  const ready = useAuthStore((s) => s.permissionsStatus === 'ready')
  // Belum dimuat (atau gagal dimuat) bukan berarti ditolak: jangan tampilkan fallback penolakan.
  if (!ready) return null
  return allowed ? children : fallback
}
