import {
  AppstoreOutlined,
  CheckSquareOutlined,
  DashboardOutlined,
  FileTextOutlined,
  LogoutOutlined,
  SettingOutlined,
} from '@ant-design/icons'
import { Button, Flex, Layout, Menu, Typography } from 'antd'
import type { MenuProps } from 'antd'
import { useState } from 'react'
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { authApi } from '@/features/auth/api/authApi'
import { useAuthStore } from '@/features/auth/store/authStore'

const { Header, Sider, Content } = Layout

// Menu selain Dashboard dinonaktifkan sampai halamannya dibuat di story fitur masing-masing.
const menuItems: MenuProps['items'] = [
  { key: '/', icon: <DashboardOutlined />, label: <Link to="/">Dashboard</Link> },
  { key: '/projects', icon: <AppstoreOutlined />, label: 'Proyek', disabled: true },
  { key: '/tasks/my', icon: <CheckSquareOutlined />, label: 'Tugas', disabled: true },
  { key: '/sops', icon: <FileTextOutlined />, label: 'SOP', disabled: true },
  { key: '/admin', icon: <SettingOutlined />, label: 'Admin', disabled: true },
]

export function MainLayout() {
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const user = useAuthStore((s) => s.user)
  const clear = useAuthStore((s) => s.clear)
  const [loggingOut, setLoggingOut] = useState(false)

  const logout = async () => {
    setLoggingOut(true)
    try {
      await authApi.logout()
    } catch {
      // Sesi lokal tetap diakhiri walau server tidak terjangkau; cookie akan ditolak saat dipakai lagi.
    } finally {
      clear()
      navigate('/login', { replace: true })
    }
  }

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider breakpoint="lg" collapsedWidth={0} theme="light" aria-label="Sidebar">
        <div style={{ padding: 16, fontWeight: 700, fontSize: 18 }}>NexUS</div>
        <Menu mode="inline" selectedKeys={[pathname]} items={menuItems} />
      </Sider>
      <Layout>
        <Header style={{ background: 'transparent', padding: '0 24px' }}>
          <Flex justify="end" align="center" gap={12} style={{ height: '100%' }}>
            {user && <Typography.Text type="secondary">Tenant {user.tenantId}</Typography.Text>}
            <Button icon={<LogoutOutlined />} onClick={logout} loading={loggingOut}>
              Keluar
            </Button>
          </Flex>
        </Header>
        <Content style={{ margin: 24 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  )
}
