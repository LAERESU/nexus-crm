import { Button, Result } from 'antd'
import { Link } from 'react-router-dom'

export default function NotFoundPage() {
  return (
    <Result
      status="404"
      title="404"
      subTitle="Halaman tidak ditemukan."
      extra={
        <Link to="/">
          <Button type="primary">Kembali ke Dashboard</Button>
        </Link>
      }
    />
  )
}
