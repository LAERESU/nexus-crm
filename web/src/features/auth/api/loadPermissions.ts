import { authApi } from '@/features/auth/api/authApi'
import { useAuthStore } from '@/features/auth/store/authStore'

/** Sesi (generasi + user) yang permission-nya sedang dimuat. */
let loading: { generation: number; userId: string } | null = null

/**
 * Memuat permission user yang sedang login ke `authStore`. Satu pemuatan per sesi dan user pada satu waktu: bila access
 * token berganti selama pemuatan (refresh karena 401), request yang sedang berjalan sudah diulang dengan token
 * baru, sehingga pemicu berikutnya dilewati dan tidak ada putaran muat-refresh.
 */
export async function loadPermissions(): Promise<void> {
  const { user, generation } = useAuthStore.getState()
  if (!user || (loading?.generation === generation && loading.userId === user.id)) return

  const current = { generation, userId: user.id }
  loading = current
  useAuthStore.getState().retryPermissions(generation)
  try {
    const permissions = await authApi.permissions()
    useAuthStore.getState().setPermissions(generation, user.id, permissions)
  } catch {
    useAuthStore.getState().failPermissions(generation, user.id)
  } finally {
    if (loading === current) loading = null
  }
}
