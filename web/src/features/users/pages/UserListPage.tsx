import { PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Flex, Input, Select, Space, Table, Tag, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import { useState } from 'react'
import { usePermission } from '@/features/auth/hooks/usePermission'
import type { User, UserStatus } from '@/features/users/api/usersApi'
import { EditUserDrawer } from '@/features/users/components/EditUserDrawer'
import { InviteUserModal } from '@/features/users/components/InviteUserModal'
import { UserStatusButton } from '@/features/users/components/UserStatusButton'
import { useDepartments, useUsers } from '@/features/users/hooks/useUsers'
import { parseProblem, problemMessage } from '@/shared/api/problemDetails'
import { useDebouncedValue } from '@/shared/hooks/useDebouncedValue'

const DEFAULT_PAGE_SIZE = 20

interface Filters {
  status?: UserStatus
  departmentId?: string
}

export default function UserListPage() {
  const canManage = usePermission('user.manage')
  const [searchText, setSearchText] = useState('')
  const [filters, setFilters] = useState<Filters>({})
  // Halaman berlaku untuk kata kunci saat halaman itu dipilih; kata kunci baru selalu mulai dari halaman 1,
  // tanpa request antara dengan pasangan halaman/kata kunci yang tidak cocok.
  const [paging, setPaging] = useState({ page: 1, forSearch: '' })
  const [size, setSize] = useState(DEFAULT_PAGE_SIZE)
  const [inviteOpen, setInviteOpen] = useState(false)
  const [editing, setEditing] = useState<User | null>(null)

  const search = useDebouncedValue(searchText.trim())
  const page = paging.forSearch === search ? paging.page : 1
  const goToPage = (next: number) => setPaging({ page: next, forSearch: search })

  const users = useUsers({
    search: search || undefined,
    status: filters.status,
    departmentId: filters.departmentId,
    page,
    size,
  })
  const departments = useDepartments()
  const departmentItems = departments.data?.items ?? []
  const items = users.data?.items ?? []
  const totalCount = users.data?.totalCount ?? 0

  // Data menyusut (user dinonaktifkan di bawah filter, diubah admin lain): mundur ke halaman terakhir yang ada.
  const lastPage = Math.max(1, Math.ceil(totalCount / size))
  if (users.data && !users.isPlaceholderData && items.length === 0 && page > lastPage)
    goToPage(lastPage)

  const changeFilters = (change: Partial<Filters>) => {
    setFilters((current) => ({ ...current, ...change }))
    goToPage(1)
  }

  // Baris terbaru dari daftar (rowVersion segar setelah dimuat ulang); snapshot dipakai bila barisnya tidak lagi tampil.
  const editingUser = editing && (items.find((u) => u.id === editing.id) ?? editing)

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
  ]
  if (canManage)
    columns.push({
      title: 'Aksi',
      key: 'actions',
      render: (_, user) => (
        <Space>
          <Button size="small" onClick={() => setEditing(user)}>
            Edit
          </Button>
          <UserStatusButton user={user} />
        </Space>
      ),
    })

  return (
    <>
      <Flex justify="space-between" align="center" wrap gap={12}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Pengguna
        </Typography.Title>
        {canManage && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setInviteOpen(true)}>
            Undang user
          </Button>
        )}
      </Flex>

      <Flex gap={12} wrap style={{ margin: '16px 0' }}>
        <Input
          aria-label="Cari user"
          placeholder="Cari nama atau email"
          prefix={<SearchOutlined />}
          allowClear
          maxLength={200}
          style={{ width: 280 }}
          value={searchText}
          onChange={(e) => setSearchText(e.target.value)}
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

      {departments.isError && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message="Daftar departemen gagal dimuat."
          action={
            <Button size="small" onClick={() => departments.refetch()}>
              Coba lagi
            </Button>
          }
        />
      )}
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
        dataSource={items}
        loading={users.isFetching}
        scroll={{ x: true }}
        pagination={{
          current: page,
          pageSize: size,
          total: totalCount,
          showSizeChanger: true,
          pageSizeOptions: [10, 20, 50, 100],
          showTotal: (total) => `${total} user`,
          onChange: (nextPage, nextSize) => {
            // Ganti ukuran halaman kembali ke halaman pertama.
            goToPage(nextSize === size ? nextPage : 1)
            setSize(nextSize)
          },
        }}
      />

      {canManage && (
        <>
          <InviteUserModal
            open={inviteOpen}
            departments={departmentItems}
            onClose={() => setInviteOpen(false)}
          />
          <EditUserDrawer
            user={editingUser}
            departments={departmentItems}
            onClose={() => setEditing(null)}
          />
        </>
      )}
    </>
  )
}
