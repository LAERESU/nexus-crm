import { Spin } from 'antd'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '@/features/auth/store/authStore'

/** Route terproteksi: tunggu pemulihan sesi, lalu arahkan ke /login bila belum login (tampilan saja; otorisasi di backend). */
export function RequireAuth() {
  const status = useAuthStore((s) => s.status)
  const location = useLocation()

  if (status === 'unknown') return <Spin fullscreen tip="Memuat sesi..." />
  if (status === 'anonymous') return <Navigate to="/login" state={{ from: location }} replace />
  return <Outlet />
}
