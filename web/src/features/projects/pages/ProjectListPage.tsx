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
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { usePermission } from '@/features/auth/hooks/usePermission'
import { useAuthStore } from '@/features/auth/store/authStore'
import {
  projectPriorityLabels,
  projectStatuses,
  projectStatusLabels,
} from '@/features/projects/api/projectsApi'
import type { Project, ProjectPriority, ProjectStatus } from '@/features/projects/api/projectsApi'
import { ProjectFormDrawer } from '@/features/projects/components/ProjectFormDrawer'
import { useArchiveProject, useProjects } from '@/features/projects/hooks/useProjects'
import { useDepartments } from '@/features/users/hooks/useUsers'
import { parseProblem, problemMessage } from '@/shared/api/problemDetails'
import { usePagedSearch } from '@/shared/hooks/usePagedSearch'

const ARCHIVE_CONFLICT_MESSAGE =
  'Proyek sudah diubah orang lain, jadi belum diarsipkan. Daftar dimuat ulang; periksa lalu coba lagi.'

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

/** Drawer: null tertutup, 'new' membuat proyek, selain itu snapshot proyek yang diubah. */
type DrawerTarget = null | 'new' | Project

export default function ProjectListPage() {
  const { message } = App.useApp()
  const canCreate = usePermission('project.create')
  const canUpdate = usePermission('project.update')
  // Daftar departemen dari User Service butuh user.read; tanpa itu filter dan kolom departemen disembunyikan.
  const canReadDepartments = usePermission('user.read')
  const userId = useAuthStore((s) => s.user?.id)

  const paging = usePagedSearch()
  const [filters, setFilters] = useState<Filters>({ mine: false })
  const [drawer, setDrawer] = useState<DrawerTarget>(null)

  const projects = useProjects({
    search: paging.search,
    status: filters.status,
    departmentId: filters.departmentId,
    ownerId: filters.mine && userId ? userId : undefined,
    page: paging.page,
    size: paging.size,
  })
  const departments = useDepartments(canReadDepartments)
  const archive = useArchiveProject()
  const departmentItems = useMemo(() => departments.data?.items ?? [], [departments.data])
  const departmentNames = useMemo(
    () => new Map(departmentItems.map((d) => [d.id, d.name])),
    [departmentItems],
  )
  const items = projects.data?.items ?? []
  paging.clampToLastPage(projects.data, projects.isPlaceholderData)

  const changeFilters = (change: Partial<Filters>) => {
    setFilters((current) => ({ ...current, ...change }))
    paging.goToPage(1)
  }

  // Baris terbaru dari daftar (rowVersion segar setelah dimuat ulang); snapshot dipakai bila barisnya tidak lagi tampil.
  const editing =
    drawer && drawer !== 'new' ? (items.find((p) => p.id === drawer.id) ?? drawer) : null

  const archiveProject = (project: Project) =>
    archive.mutate(project, {
      onSuccess: () => message.success(`Proyek ${project.code} diarsipkan.`),
      onError: (error) => {
        const problem = parseProblem(error)
        message.error(problem.status === 409 ? ARCHIVE_CONFLICT_MESSAGE : problemMessage(problem))
      },
    })

  const columns: TableColumnsType<Project> = [
    { title: 'Kode', dataIndex: 'code', width: 120 },
    {
      title: 'Nama',
      dataIndex: 'name',
      render: (name: string, p) => <Link to={`/projects/${p.id}`}>{name}</Link>,
    },
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
  ]
  if (canReadDepartments)
    columns.push({
      title: 'Departemen',
      dataIndex: 'departmentId',
      render: (value: string | null) =>
        value ? (departmentNames.get(value) ?? (departments.isError ? '?' : '-')) : '-',
    })
  if (canUpdate)
    columns.push({
      title: 'Aksi',
      key: 'actions',
      render: (_, project) =>
        project.status === 'archived' ? null : (
          <Space>
            <Button size="small" onClick={() => setDrawer(project)}>
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
          value={paging.searchText}
          onChange={(e) => paging.setSearchText(e.target.value)}
        />
        <Select<ProjectStatus>
          aria-label="Filter status"
          placeholder="Semua status (tanpa arsip)"
          allowClear
          style={{ width: 220 }}
          value={filters.status}
          onChange={(status) => changeFilters({ status })}
          options={projectStatuses.map((value) => ({ value, label: projectStatusLabels[value] }))}
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
          disabled={!userId}
          onChange={(e) => changeFilters({ mine: e.target.checked })}
        >
          Hanya milik saya
        </Checkbox>
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
          current: paging.page,
          pageSize: paging.size,
          total: projects.data?.totalCount ?? 0,
          showSizeChanger: true,
          pageSizeOptions: [10, 20, 50, 100],
          showTotal: (total) => `${total} proyek`,
          onChange: paging.changePage,
        }}
      />

      {drawer !== null && (
        <ProjectFormDrawer
          key={drawer === 'new' ? 'new' : drawer.id}
          project={editing}
          departments={departmentItems}
          onClose={() => setDrawer(null)}
        />
      )}
    </>
  )
}
