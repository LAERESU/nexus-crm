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
import { grantsPermission, useAuthStore } from '@/features/auth/store/authStore'

const { Header, Sider, Content } = Layout

type MenuItem = NonNullable<MenuProps['items']>[number]

/**
 * Menu yang halamannya belum dibuat dinonaktifkan sampai story fiturnya dikerjakan. Menu yang butuh permission
 * hanya tampil bila user memegangnya (tampilan saja; otorisasi tetap di backend).
 */
function buildMenu(can: (permission: string) => boolean): MenuItem[] {
  const admin: MenuItem[] = []
  if (can('user.read'))
    admin.push({ key: '/admin/users', label: <Link to="/admin/users">Pengguna</Link> })

  const items: MenuItem[] = [
    { key: '/', icon: <DashboardOutlined />, label: <Link to="/">Dashboard</Link> },
    { key: '/projects', icon: <AppstoreOutlined />, label: 'Proyek', disabled: true },
    { key: '/tasks/my', icon: <CheckSquareOutlined />, label: 'Tugas', disabled: true },
    { key: '/sops', icon: <FileTextOutlined />, label: 'SOP', disabled: true },
  ]
  if (admin.length > 0)
    items.push({ key: '/admin', icon: <SettingOutlined />, label: 'Admin', children: admin })
  return items
}

export function MainLayout() {
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const user = useAuthStore((s) => s.user)
  const clear = useAuthStore((s) => s.clear)
  const permissions = useAuthStore((s) => s.permissions)
  const menuItems = buildMenu((permission) => grantsPermission(permissions, permission))
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
        <Menu
          mode="inline"
          selectedKeys={[pathname.replace(/(.)\/+$/, '$1')]}
          defaultOpenKeys={pathname.startsWith('/admin') ? ['/admin'] : []}
          items={menuItems}
        />
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
