import { Typography } from 'antd'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { LoginForm } from '@/features/auth/components/LoginForm'
import { useAuthStore } from '@/features/auth/store/authStore'

interface LocationState {
  from?: { pathname?: string; search?: string; hash?: string }
}

export default function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const status = useAuthStore((s) => s.status)
  const from = (location.state as LocationState | null)?.from
  const target = from?.pathname ? `${from.pathname}${from.search ?? ''}${from.hash ?? ''}` : '/'

  if (status === 'authenticated') return <Navigate to={target} replace />

  return (
    <>
      <Typography.Title level={3} style={{ textAlign: 'center' }}>
        Masuk ke NexUS
      </Typography.Title>
      <LoginForm onSuccess={() => navigate(target, { replace: true })} />
    </>
  )
}
