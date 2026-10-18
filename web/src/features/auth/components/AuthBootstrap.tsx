import { useEffect } from 'react'
import type { ReactNode } from 'react'
import { authApi } from '@/features/auth/api/authApi'
import { useAuthStore } from '@/features/auth/store/authStore'

/**
 * Saat aplikasi dimuat, sesi dipulihkan dari refresh token (cookie httpOnly) karena access token hanya ada di
 * memori dan hilang ketika halaman di-refresh.
 */
export function AuthBootstrap({ children }: { children: ReactNode }) {
  const status = useAuthStore((s) => s.status)

  useEffect(() => {
    if (status !== 'unknown') return
    // Gagal (belum login/sesi berakhir) → refreshAccessToken sudah mengubah status menjadi anonymous.
    authApi.restoreSession().catch(() => undefined)
  }, [status])

  return children
}
