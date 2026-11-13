import { authApi } from '@/features/auth/api/authApi'
import { useAuthStore } from '@/features/auth/store/authStore'

/** Generasi sesi yang permission-nya sedang dimuat. */
let loadingGeneration: number | null = null

/**
 * Memuat permission user yang sedang login ke `authStore`. Satu pemuatan per sesi pada satu waktu: bila access
 * token berganti selama pemuatan (refresh karena 401), request yang sedang berjalan sudah diulang dengan token
 * baru, sehingga pemicu berikutnya dilewati dan tidak ada putaran muat-refresh.
 */
export async function loadPermissions(): Promise<void> {
  const { user, generation } = useAuthStore.getState()
  if (!user || loadingGeneration === generation) return

  loadingGeneration = generation
  useAuthStore.getState().retryPermissions(generation)
  try {
    const permissions = await authApi.permissions(user.id)
    useAuthStore.getState().setPermissions(generation, permissions)
  } catch {
    useAuthStore.getState().failPermissions(generation)
  } finally {
    if (loadingGeneration === generation) loadingGeneration = null
  }
}
