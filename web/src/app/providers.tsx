import { QueryClientProvider } from '@tanstack/react-query'
import { App as AntdApp, ConfigProvider } from 'antd'
import type { ReactNode } from 'react'
import { AuthBootstrap } from '@/features/auth/components/AuthBootstrap'
import { queryClient } from '@/shared/api/queryClient'

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <ConfigProvider>
        <AntdApp>
          <AuthBootstrap>{children}</AuthBootstrap>
        </AntdApp>
      </ConfigProvider>
    </QueryClientProvider>
  )
}
