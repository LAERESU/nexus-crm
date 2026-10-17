import { Button, Result } from 'antd'
import { Link } from 'react-router-dom'

export default function ForbiddenPage() {
  return (
    <Result
      status="403"
      title="403"
      subTitle="Anda tidak memiliki akses ke halaman ini."
      extra={
        <Link to="/">
          <Button type="primary">Kembali ke Dashboard</Button>
        </Link>
      }
    />
  )
}
