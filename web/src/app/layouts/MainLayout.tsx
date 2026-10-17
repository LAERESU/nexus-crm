import {
  AppstoreOutlined,
  CheckSquareOutlined,
  DashboardOutlined,
  FileTextOutlined,
  SettingOutlined,
} from '@ant-design/icons'
import { Layout, Menu } from 'antd'
import type { MenuProps } from 'antd'
import { Link, Outlet, useLocation } from 'react-router-dom'

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

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider breakpoint="lg" collapsedWidth={0} theme="light" aria-label="Sidebar">
        <div style={{ padding: 16, fontWeight: 700, fontSize: 18 }}>NexUS</div>
        <Menu mode="inline" selectedKeys={[pathname]} items={menuItems} />
      </Sider>
      <Layout>
        <Header style={{ background: 'transparent', padding: '0 24px' }} />
        <Content style={{ margin: 24 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  )
}
