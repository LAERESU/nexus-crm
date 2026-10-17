import { Card, Flex } from 'antd'
import { Outlet } from 'react-router-dom'

export function AuthLayout() {
  return (
    <Flex align="center" justify="center" style={{ minHeight: '100vh' }}>
      <Card style={{ width: 400 }}>
        <Outlet />
      </Card>
    </Flex>
  )
}
