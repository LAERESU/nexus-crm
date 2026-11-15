import { PlusOutlined, SearchOutlined } from '@ant-design/icons'
import {
  Alert,
  App,
  Button,
  Checkbox,
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
import { usePermission } from '@/features/auth/hooks/usePermission'
import { useAuthStore } from '@/features/auth/store/authStore'
import {
  CONFLICT_MESSAGE,
  projectPriorityLabels,
  projectStatusLabels,
} from '@/features/projects/api/projectsApi'
import type { Project, ProjectPriority, ProjectStatus } from '@/features/projects/api/projectsApi'
import { ProjectFormDrawer } from '@/features/projects/components/ProjectFormDrawer'
import { useArchiveProject, useProjects } from '@/features/projects/hooks/useProjects'
import { useDepartments } from '@/features/users/hooks/useUsers'
import { parseProblem, problemMessage } from '@/shared/api/problemDetails'
import { useDebouncedValue } from '@/shared/hooks/useDebouncedValue'

const DEFAULT_PAGE_SIZE = 20

const statusColors: Record<ProjectStatus, string> = {
  planning: 'blue',
  active: 'green',
  on_hold: 'orange',
  completed: 'purple',
  archived: 'default',
}

const priorityColors: Record<ProjectPriority, string> = {
  low: 'default',
  medium: 'blue',
  high: 'orange',
  urgent: 'red',
}

interface Filters {
  status?: ProjectStatus
  departmentId?: string
  mine: boolean
}

/** Drawer: null tertutup, 'new' membuat proyek, selain itu ID proyek yang diubah. */
type DrawerTarget = null | 'new' | string

export default function ProjectListPage() {
  const { message } = App.useApp()
  const canCreate = usePermission('project.create')
  const canUpdate = usePermission('project.update')
  // Daftar departemen dari User Service butuh user.read; tanpa itu filter departemen disembunyikan.
  const canReadDepartments = usePermission('user.read')
  const userId = useAuthStore((s) => s.user?.id)

  const [searchText, setSearchText] = useState('')
  const [filters, setFilters] = useState<Filters>({ mine: false })
  const [paging, setPaging] = useState({ page: 1, forSearch: '' })
  const [size, setSize] = useState(DEFAULT_PAGE_SIZE)
  const [drawer, setDrawer] = useState<DrawerTarget>(null)

  const search = useDebouncedValue(searchText.trim())
  const page = paging.forSearch === search ? paging.page : 1
  const goToPage = (next: number) => setPaging({ page: next, forSearch: search })

  const projects = useProjects({
    search: search || undefined,
    status: filters.status,
    departmentId: filters.departmentId,
    ownerId: filters.mine ? userId : undefined,
    page,
    size,
  })
  const departments = useDepartments(canReadDepartments)
  const archive = useArchiveProject()
  const departmentItems = departments.data?.items ?? []
  const departmentName = (id: string | null) =>
    (id && departmentItems.find((d) => d.id === id)?.name) || '-'
  const items = projects.data?.items ?? []
  const totalCount = projects.data?.totalCount ?? 0

  // Data menyusut (proyek diarsipkan di bawah filter, diubah orang lain): mundur ke halaman terakhir yang ada.
  const lastPage = Math.max(1, Math.ceil(totalCount / size))
  if (projects.data && !projects.isPlaceholderData && items.length === 0 && page > lastPage)
    goToPage(lastPage)

  const changeFilters = (change: Partial<Filters>) => {
    setFilters((current) => ({ ...current, ...change }))
    goToPage(1)
  }

  // Baris terbaru dari daftar (rowVersion segar setelah dimuat ulang, termasuk setelah 409).
  const editing = drawer && drawer !== 'new' ? (items.find((p) => p.id === drawer) ?? null) : null

  const archiveProject = (project: Project) =>
    archive.mutate(project, {
      onSuccess: () => message.success(`Proyek ${project.code} diarsipkan.`),
      onError: (error) => {
        const problem = parseProblem(error)
        message.error(problem.status === 409 ? CONFLICT_MESSAGE : problemMessage(problem))
      },
    })

  const columns: TableColumnsType<Project> = [
    { title: 'Kode', dataIndex: 'code', width: 120 },
    { title: 'Nama', dataIndex: 'name' },
    {
      title: 'Status',
      dataIndex: 'status',
      render: (value: ProjectStatus) => (
        <Tag color={statusColors[value]}>{projectStatusLabels[value]}</Tag>
      ),
    },
    {
      title: 'Prioritas',
      dataIndex: 'priority',
      render: (value: ProjectPriority) => (
        <Tag color={priorityColors[value]}>{projectPriorityLabels[value]}</Tag>
      ),
    },
    {
      title: 'Periode',
      key: 'period',
      render: (_, p) =>
        p.startDate || p.endDate ? `${p.startDate ?? '…'} s.d. ${p.endDate ?? '…'}` : '-',
    },
    ...(canReadDepartments
      ? [
          {
            title: 'Departemen',
            dataIndex: 'departmentId',
            render: (value: string | null) => departmentName(value),
          },
        ]
      : []),
  ]
  if (canUpdate)
    columns.push({
      title: 'Aksi',
      key: 'actions',
      render: (_, project) =>
        project.status === 'archived' ? null : (
          <Space>
            <Button size="small" onClick={() => setDrawer(project.id)}>
              Ubah
            </Button>
            <Popconfirm
              title={`Arsipkan ${project.code}?`}
              description="Proyek yang diarsipkan tidak bisa diubah lagi."
              okText="Arsipkan"
              okButtonProps={{ danger: true }}
              cancelText="Batal"
              onConfirm={() => archiveProject(project)}
            >
              <Button
                size="small"
                danger
                loading={archive.isPending && archive.variables.id === project.id}
              >
                Arsipkan
              </Button>
            </Popconfirm>
          </Space>
        ),
    })

  return (
    <>
      <Flex justify="space-between" align="center" wrap gap={12}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Proyek
        </Typography.Title>
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer('new')}>
            Proyek baru
          </Button>
        )}
      </Flex>

      <Flex gap={12} wrap align="center" style={{ margin: '16px 0' }}>
        <Input
          aria-label="Cari proyek"
          placeholder="Cari nama atau kode"
          prefix={<SearchOutlined />}
          allowClear
          maxLength={200}
          style={{ width: 260 }}
          value={searchText}
          onChange={(e) => setSearchText(e.target.value)}
        />
        <Select<ProjectStatus>
          aria-label="Filter status"
          placeholder="Semua status (tanpa arsip)"
          allowClear
          style={{ width: 220 }}
          value={filters.status}
          onChange={(status) => changeFilters({ status })}
          options={(Object.keys(projectStatusLabels) as ProjectStatus[]).map((value) => ({
            value,
            label: projectStatusLabels[value],
          }))}
        />
        {canReadDepartments && (
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
        )}
        <Checkbox
          checked={filters.mine}
          onChange={(e) => changeFilters({ mine: e.target.checked })}
        >
          Hanya milik saya
        </Checkbox>
      </Flex>

      {projects.isError && (
        <Alert
          type="error"
          showIcon
          role="alert"
          style={{ marginBottom: 16 }}
          message={problemMessage(parseProblem(projects.error))}
          action={
            <Button size="small" onClick={() => projects.refetch()}>
              Coba lagi
            </Button>
          }
        />
      )}

      <Table<Project>
        rowKey="id"
        columns={columns}
        dataSource={items}
        loading={projects.isFetching}
        scroll={{ x: true }}
        pagination={{
          current: page,
          pageSize: size,
          total: totalCount,
          showSizeChanger: true,
          pageSizeOptions: [10, 20, 50, 100],
          showTotal: (total) => `${total} proyek`,
          onChange: (nextPage, nextSize) => {
            goToPage(nextSize === size ? nextPage : 1)
            setSize(nextSize)
          },
        }}
      />

      {(canCreate || canUpdate) && (
        <ProjectFormDrawer
          open={drawer === 'new' || editing !== null}
          project={editing}
          departments={departmentItems}
          onClose={() => setDrawer(null)}
        />
      )}
    </>
  )
}
