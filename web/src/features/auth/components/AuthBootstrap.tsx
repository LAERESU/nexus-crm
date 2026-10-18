import { useEffect } from 'react'
import type { ReactNode } from 'react'
import { authApi } from '@/features/auth/api/authApi'
import { useAuthStore } from '@/features/auth/store/authStore'
import { queryClient } from '@/shared/api/queryClient'

/**
 * Saat aplikasi dimuat, sesi dipulihkan dari refresh token (cookie httpOnly) karena access token hanya ada di
 * memori dan hilang ketika halaman di-refresh. Cache data server dibersihkan setiap kali sesi berakhir atau user
 * berganti, agar user berikutnya di tab yang sama tidak melihat data user/tenant sebelumnya.
 */
export function AuthBootstrap({ children }: { children: ReactNode }) {
  useEffect(
    () =>
      useAuthStore.subscribe((state, previous) => {
        const ended = previous.status === 'authenticated' && state.status !== 'authenticated'
        const switched =
          previous.user !== null && state.user !== null && previous.user.id !== state.user.id
        if (ended || switched) queryClient.clear()
      }),
    [],
  )

  useEffect(() => {
    if (useAuthStore.getState().status !== 'unknown') return
    const startedAt = useAuthStore.getState().generation
    authApi.restoreSession().catch(() => {
      // 401 sudah mengubah status menjadi anonymous. Error lain (jaringan/5xx): tampilkan login daripada memuat
      // selamanya; sesi di server tetap utuh dan bisa dipulihkan dengan memuat ulang halaman.
      const store = useAuthStore.getState()
      if (store.status === 'unknown' && store.generation === startedAt) store.clear()
    })
  }, [])

  return children
}
