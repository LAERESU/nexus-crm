import { Suspense } from 'react'
import type { ReactNode } from 'react'
import { Spin } from 'antd'
import { createBrowserRouter } from 'react-router-dom'
import { AuthLayout } from '@/app/layouts/AuthLayout'
import { MainLayout } from '@/app/layouts/MainLayout'
import { DashboardPage, ForbiddenPage, LoginPage, NotFoundPage } from '@/app/lazyPages'
import { RequireAuth } from '@/features/auth/components/RequireAuth'

const withSuspense = (element: ReactNode) => (
  <Suspense fallback={<Spin fullscreen />}>{element}</Suspense>
)

export const routes = [
  {
    element: <AuthLayout />,
    children: [{ path: 'login', element: withSuspense(<LoginPage />) }],
  },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <MainLayout />,
        children: [
          { index: true, element: withSuspense(<DashboardPage />) },
          { path: '403', element: withSuspense(<ForbiddenPage />) },
          { path: '*', element: withSuspense(<NotFoundPage />) },
        ],
      },
    ],
  },
]

export const router = createBrowserRouter(routes)
