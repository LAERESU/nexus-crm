import { lazy } from 'react'

export const DashboardPage = lazy(() => import('@/features/dashboard/pages/DashboardPage'))
export const ForbiddenPage = lazy(() => import('@/features/errors/pages/ForbiddenPage'))
export const NotFoundPage = lazy(() => import('@/features/errors/pages/NotFoundPage'))
export const UserListPage = lazy(() => import('@/features/users/pages/UserListPage'))
export const LoginPage = lazy(() => import('@/features/auth/pages/LoginPage'))
