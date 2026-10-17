import { Suspense } from 'react'
import type { ReactNode } from 'react'
import { Spin } from 'antd'
import { createBrowserRouter } from 'react-router-dom'
import { MainLayout } from '@/app/layouts/MainLayout'
import { DashboardPage, ForbiddenPage, NotFoundPage } from '@/app/lazyPages'

const withSuspense = (element: ReactNode) => (
  <Suspense fallback={<Spin fullscreen />}>{element}</Suspense>
)

export const routes = [
  {
    element: <MainLayout />,
    children: [
      { index: true, element: withSuspense(<DashboardPage />) },
      { path: '403', element: withSuspense(<ForbiddenPage />) },
      { path: '*', element: withSuspense(<NotFoundPage />) },
    ],
  },
]

export const router = createBrowserRouter(routes)
