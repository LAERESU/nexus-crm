import { PlusOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Alert,
  App,
  Button,
  Flex,
  Input,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd'
import type { TableColumnsType } from 'antd'
import { useState } from 'react'
import type { User, UserStatus } from '@/features/users/api/usersApi'
import { EditUserDrawer } from '@/features/users/components/EditUserDrawer'
import { InviteUserModal } from '@/features/users/components/InviteUserModal'
import { useDepartments, useSetUserStatus, useUsers } from '@/features/users/hooks/useUsers'
import { parseProblem, problemMessage } from '@/shared/api/problemDetails'
import { useDebouncedValue } from '@/shared/hooks/useDebouncedValue'

const DEFAULT_PAGE_SIZE = 20

interface Filters {
  search: string
  status?: UserStatus
  departmentId?: string
}

export default function UserListPage() {
  const { message } = App.useApp()
  const [filters, setFilters] = useState<Filters>({ search: '' })
  const [page, setPage] = useState(1)
  const [size, setSize] = useState(DEFAULT_PAGE_SIZE)
  const [inviteOpen, setInviteOpen] = useState(false)
  const [editing, setEditing] = useState<User | null>(null)

  const search = useDebouncedValue(filters.search.trim())
  const users = useUsers({
    search: search || undefined,
    status: filters.status,
    departmentId: filters.departmentId,
    page,
    size,
  })
  const departments = useDepartments()
  const setStatus = useSetUserStatus()
  const departmentItems = departments.data?.items ?? []

  /** Mengubah filter selalu kembali ke halaman pertama. */
  const changeFilters = (change: Partial<Filters>) => {
    setFilters((current) => ({ ...current, ...change }))
    setPage(1)
  }

  const toggleStatus = (user: User) => {
    const status: UserStatus = user.status === 'active' ? 'inactive' : 'active'
    setStatus.mutate(
      { user, status },
      {
        onSuccess: () =>
          message.success(
            status === 'active'
              ? `${user.fullName} diaktifkan.`
              : `${user.fullName} dinonaktifkan.`,
          ),
        onError: (error) => message.error(problemMessage(parseProblem(error))),
      },
    )
  }

  const columns: TableColumnsType<User> = [
    { title: 'Nama', dataIndex: 'fullName' },
    { title: 'Email', dataIndex: 'email' },
    { title: 'Jabatan', dataIndex: 'jobTitle', render: (value: string | null) => value ?? '-' },
    {
      title: 'Departemen',
      dataIndex: 'departmentName',
      render: (value: string | null) => value ?? '-',
    },
    {
      title: 'Status',
      dataIndex: 'status',
      render: (value: UserStatus) =>
        value === 'active' ? <Tag color="green">Aktif</Tag> : <Tag>Nonaktif</Tag>,
    },
    {
      title: 'Aksi',
      key: 'actions',
      render: (_, user) => (
        <Space>
          <Button size="small" onClick={() => setEditing(user)}>
            Edit
          </Button>
          <Popconfirm
            title={
              user.status === 'active'
                ? `Nonaktifkan ${user.fullName}?`
                : `Aktifkan ${user.fullName}?`
            }
            okText="Ya"
            cancelText="Batal"
            onConfirm={() => toggleStatus(user)}
          >
            <Button
              size="small"
              danger={user.status === 'active'}
              loading={setStatus.isPending && setStatus.variables.user.id === user.id}
            >
              {user.status === 'active' ? 'Nonaktifkan' : 'Aktifkan'}
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <>
      <Flex justify="space-between" align="center" wrap gap={12}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Pengguna
        </Typography.Title>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => setInviteOpen(true)}>
          Undang user
        </Button>
      </Flex>

      <Flex gap={12} wrap style={{ margin: '16px 0' }}>
        <Input
          aria-label="Cari user"
          placeholder="Cari nama atau email"
          prefix={<SearchOutlined />}
          allowClear
          maxLength={200}
          style={{ width: 280 }}
          value={filters.search}
          onChange={(e) => changeFilters({ search: e.target.value })}
        />
        <Select<UserStatus>
          aria-label="Filter status"
          placeholder="Semua status"
          allowClear
          style={{ width: 160 }}
          value={filters.status}
          onChange={(status) => changeFilters({ status })}
          options={[
            { value: 'active', label: 'Aktif' },
            { value: 'inactive', label: 'Nonaktif' },
          ]}
        />
        <Select<string>
          aria-label="Filter departemen"
          placeholder="Semua departemen"
          allowClear
          showSearch
          optionFilterProp="label"
          style={{ width: 220 }}
          loading={departments.isLoading}
          value={filters.departmentId}
          onChange={(departmentId) => changeFilters({ departmentId })}
          options={departmentItems.map((d) => ({ value: d.id, label: d.name }))}
        />
      </Flex>

      {users.isError && (
        <Alert
          type="error"
          showIcon
          role="alert"
          style={{ marginBottom: 16 }}
          message={problemMessage(parseProblem(users.error))}
          action={
            <Button size="small" onClick={() => users.refetch()}>
              Coba lagi
            </Button>
          }
        />
      )}

      <Table<User>
        rowKey="id"
        columns={columns}
        dataSource={users.data?.items ?? []}
        loading={users.isFetching}
        scroll={{ x: true }}
        pagination={{
          current: page,
          pageSize: size,
          total: users.data?.totalCount ?? 0,
          showSizeChanger: true,
          pageSizeOptions: [10, 20, 50, 100],
          showTotal: (total) => `${total} user`,
          onChange: (nextPage, nextSize) => {
            // Ganti ukuran halaman kembali ke halaman pertama.
            setPage(nextSize === size ? nextPage : 1)
            setSize(nextSize)
          },
        }}
      />

      <InviteUserModal
        open={inviteOpen}
        departments={departmentItems}
        onClose={() => setInviteOpen(false)}
      />
      <EditUserDrawer
        user={editing}
        departments={departmentItems}
        onClose={() => setEditing(null)}
      />
    </>
  )
}
