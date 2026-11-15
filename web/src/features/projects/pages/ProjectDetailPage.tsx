import { ArrowLeftOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  Descriptions,
  Empty,
  Flex,
  Progress,
  Result,
  Spin,
  Tabs,
  Tag,
  Typography,
} from 'antd'
import { Link, useParams } from 'react-router-dom'
import { usePermission } from '@/features/auth/hooks/usePermission'
import { useAuthStore } from '@/features/auth/store/authStore'
import {
  memberDisplayName,
  projectPriorityLabels,
  projectStatusLabels,
} from '@/features/projects/api/projectsApi'
import type { Project, ProjectMember } from '@/features/projects/api/projectsApi'
import { MilestoneTimeline } from '@/features/projects/components/MilestoneTimeline'
import { ProjectMembersTab } from '@/features/projects/components/ProjectMembersTab'
import {
  useMilestones,
  useProject,
  useProjectMembers,
} from '@/features/projects/hooks/useProjectDetail'
import { useDepartments } from '@/features/users/hooks/useUsers'
import { parseProblem, problemMessage } from '@/shared/api/problemDetails'

function Summary({
  project,
  members,
  departmentName,
}: {
  project: Project
  members: ProjectMember[]
  departmentName: string
}) {
  const owner = members.find((m) => m.userId === project.ownerId)
  return (
    <Descriptions column={{ xs: 1, md: 2 }} bordered size="small">
      <Descriptions.Item label="Kode">{project.code}</Descriptions.Item>
      <Descriptions.Item label="Status">{projectStatusLabels[project.status]}</Descriptions.Item>
      <Descriptions.Item label="Prioritas">
        {projectPriorityLabels[project.priority]}
      </Descriptions.Item>
      <Descriptions.Item label="Pemilik">
        {owner ? memberDisplayName(owner) : '-'}
      </Descriptions.Item>
      <Descriptions.Item label="Periode">
        {project.startDate || project.endDate
          ? `${project.startDate ?? '…'} s.d. ${project.endDate ?? '…'}`
          : '-'}
      </Descriptions.Item>
      <Descriptions.Item label="Departemen">{departmentName}</Descriptions.Item>
      <Descriptions.Item label="Progres" span={2}>
        <Progress percent={project.progressPct} size="small" style={{ maxWidth: 320 }} />
      </Descriptions.Item>
      <Descriptions.Item label="Deskripsi" span={2}>
        {project.description ?? '-'}
      </Descriptions.Item>
    </Descriptions>
  )
}

/** Detail proyek: tab Ringkasan, Anggota, Milestone; Board dan SOP disiapkan untuk E4/E6. */
export default function ProjectDetailPage() {
  const { id = '' } = useParams()
  const userId = useAuthStore((s) => s.user?.id)
  const canManageMembers = usePermission('project.member.manage')
  const canUpdate = usePermission('project.update')
  const seesAll = usePermission('project.all.read')
  const canReadDepartments = usePermission('user.read')

  const project = useProject(id)
  const loaded = project.isSuccess
  const members = useProjectMembers(id, loaded)
  const milestones = useMilestones(id, loaded)
  const departments = useDepartments(canReadDepartments && !!project.data?.departmentId)

  // Tampilan saja; backend tetap memeriksa peran MANAGER (atau admin tenant) di setiap perubahan.
  const memberItems = members.data ?? []
  const isManager = memberItems.some((m) => m.userId === userId && m.role === 'manager')
  const canManageProject = isManager || seesAll

  if (project.isPending) return <Spin size="large" aria-label="Memuat proyek" />
  // Gagal memuat ulang di latar belakang tidak mengganti halaman yang sudah tampil (data lama tetap dipakai).
  if (!project.data) {
    const status = parseProblem(project.error).status
    return (
      <Result
        status={status === 403 ? '403' : status === 404 ? '404' : 'error'}
        title={
          status === 403
            ? 'Anda bukan anggota proyek ini'
            : status === 404
              ? 'Proyek tidak ditemukan'
              : 'Proyek gagal dimuat'
        }
        subTitle={
          status === 403 || status === 404 ? undefined : problemMessage(parseProblem(project.error))
        }
        extra={
          <Link to="/projects">
            <Button type="primary">Kembali ke daftar proyek</Button>
          </Link>
        }
      />
    )
  }

  const data = project.data
  // Hak kelola belum diketahui selama daftar anggota belum berhasil dimuat.
  const manageUnknown = !members.isSuccess
  const departmentName =
    (data.departmentId && departments.data?.items.find((d) => d.id === data.departmentId)?.name) ||
    '-'

  return (
    <>
      <Flex align="center" gap={12} wrap style={{ marginBottom: 16 }}>
        <Link to="/projects" aria-label="Kembali ke daftar proyek">
          <ArrowLeftOutlined />
        </Link>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {data.name}
        </Typography.Title>
        <Tag>{data.code}</Tag>
        {data.status === 'archived' && <Tag>Diarsipkan</Tag>}
      </Flex>
      {members.isError && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
          message={`Anggota gagal dimuat: ${problemMessage(parseProblem(members.error))}`}
          action={
            <Button size="small" onClick={() => void members.refetch()}>
              Coba lagi
            </Button>
          }
        />
      )}
      <Tabs
        items={[
          {
            key: 'summary',
            label: 'Ringkasan',
            children: (
              <Summary project={data} members={memberItems} departmentName={departmentName} />
            ),
          },
          {
            key: 'members',
            label: `Anggota (${members.data?.length ?? '…'})`,
            children: (
              <ProjectMembersTab
                projectId={id}
                members={memberItems}
                loading={members.isFetching}
                canManage={canManageMembers && canManageProject}
                manageUnknown={manageUnknown}
                readOnly={data.status === 'archived'}
              />
            ),
          },
          {
            key: 'milestones',
            label: 'Milestone',
            children: (
              <MilestoneTimeline
                project={data}
                milestones={milestones.data ?? []}
                loading={milestones.isFetching}
                error={milestones.isError && !milestones.data ? milestones.error : null}
                onRetry={() => void milestones.refetch()}
                canManage={canUpdate && canManageProject}
              />
            ),
          },
          {
            key: 'board',
            label: 'Board',
            children: <Empty description="Board tugas tersedia setelah modul Tugas (E4)." />,
          },
          {
            key: 'sops',
            label: 'SOP',
            children: <Empty description="SOP terkait tersedia setelah modul SOP (E6)." />,
          },
        ]}
      />
    </>
  )
}
