import type { ReactNode } from 'react'
import { usePermission } from '@/features/auth/hooks/usePermission'

interface CanProps {
  permission: string
  children: ReactNode
  /** Ditampilkan bila user tidak memegang permission (bawaan: tidak ada). */
  fallback?: ReactNode
}

/** Menyembunyikan aksi yang tidak boleh dipakai user (tampilan saja; otorisasi tetap di backend); selama permission belum dimuat, aksi tersembunyi. */
export function Can({ permission, children, fallback = null }: CanProps) {
  return usePermission(permission) ? children : fallback
}
