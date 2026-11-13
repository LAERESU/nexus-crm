import { Button, Result, Spin } from 'antd'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { loadPermissions } from '@/features/auth/api/loadPermissions'
import { grantsPermission, useAuthStore } from '@/features/auth/store/authStore'

/**
 * Route terproteksi: tunggu pemulihan sesi, lalu arahkan ke /login bila belum login. Dengan `permission`, user yang
 * tidak memegangnya diarahkan ke /403. Ini tampilan saja; otorisasi tetap di backend.
 */
export function RequireAuth({ permission }: { permission?: string }) {
  const status = useAuthStore((s) => s.status)
  const permissionsStatus = useAuthStore((s) => s.permissionsStatus)
  const allowed = useAuthStore((s) => !permission || grantsPermission(s.permissions, permission))
  const location = useLocation()

  if (status === 'unknown') return <Spin fullscreen tip="Memuat sesi..." />
  if (status === 'anonymous') return <Navigate to="/login" state={{ from: location }} replace />
  if (!permission) return <Outlet />

  if (permissionsStatus === 'loading') return <Spin fullscreen tip="Memuat hak akses..." />
  if (permissionsStatus === 'error')
    return (
      <Result
        status="warning"
        title="Hak akses gagal dimuat"
        subTitle="Halaman ini belum bisa dibuka. Periksa koneksi lalu coba lagi."
        extra={
          <Button type="primary" onClick={() => void loadPermissions()}>
            Coba lagi
          </Button>
        }
      />
    )
  return allowed ? <Outlet /> : <Navigate to="/403" replace />
}
