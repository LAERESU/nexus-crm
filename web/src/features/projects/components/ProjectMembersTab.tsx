import { Alert, App, Button, Flex, Popconfirm, Select, Table, Tag } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import {
  memberDisplayName,
  projectRoleLabels,
  projectRoles,
} from '@/features/projects/api/projectsApi'
import type { ProjectMember, ProjectRole } from '@/features/projects/api/projectsApi'
import { MemberPicker } from '@/features/projects/components/MemberPicker'
import {
  useAddMember,
  useChangeMemberRole,
  useRemoveMember,
} from '@/features/projects/hooks/useProjectDetail'
import { parseProblem, problemMessage } from '@/shared/api/problemDetails'

interface ProjectMembersTabProps {
  projectId: string
  members: ProjectMember[]
  loading: boolean
  /** Manager proyek ini (atau admin tenant): boleh menambah, mengubah peran, dan mengeluarkan anggota. */
  canManage: boolean
  /** Hak kelola belum bisa ditentukan (daftar anggota belum dimuat): jangan tampilkan pesan "hanya manager". */
  manageUnknown: boolean
  /** Proyek yang diarsipkan tidak bisa diubah anggotanya. */
  readOnly: boolean
}

const roleOptions = projectRoles.map((value) => ({ value, label: projectRoleLabels[value] }))

function useFailToast() {
  const { message } = App.useApp()
  return (error: unknown) => message.error(problemMessage(parseProblem(error)))
}

/** Pilihan peran satu anggota, dengan mutasinya sendiri. */
function MemberRoleSelect({ projectId, member }: { projectId: string; member: ProjectMember }) {
  const { message } = App.useApp()
  const fail = useFailToast()
  const changeRole = useChangeMemberRole(projectId)
  return (
    <Select<ProjectRole>
      aria-label={`Peran ${memberDisplayName(member)}`}
      size="small"
      style={{ width: 130 }}
      value={member.role}
      options={roleOptions}
      loading={changeRole.isPending}
      disabled={changeRole.isPending}
      onChange={(role) =>
        changeRole.mutate(
          { userId: member.userId, role },
          {
            onSuccess: () => message.success(`Peran ${memberDisplayName(member)} diubah.`),
            onError: fail,
          },
        )
      }
    />
  )
}

/** Tombol keluarkan satu anggota, dengan mutasinya sendiri. */
function RemoveMemberButton({ projectId, member }: { projectId: string; member: ProjectMember }) {
  const { message } = App.useApp()
  const fail = useFailToast()
  const remove = useRemoveMember(projectId)
  return (
    <Popconfirm
      title={`Keluarkan ${memberDisplayName(member)} dari proyek?`}
      okText="Keluarkan"
      okButtonProps={{ danger: true }}
      cancelText="Batal"
      onConfirm={() =>
        remove.mutate(member.userId, {
          onSuccess: () => message.success(`${memberDisplayName(member)} dikeluarkan.`),
          onError: fail,
        })
      }
    >
      <Button size="small" danger loading={remove.isPending}>
        Keluarkan
      </Button>
    </Popconfirm>
  )
}

export function ProjectMembersTab({
  projectId,
  members,
  loading,
  canManage,
  manageUnknown,
  readOnly,
}: ProjectMembersTabProps) {
  const { message } = App.useApp()
  const fail = useFailToast()
  const add = useAddMember(projectId)
  const [picked, setPicked] = useState<string | null>(null)
  const [role, setRole] = useState<ProjectRole>('member')
  const editable = canManage && !readOnly
  const memberIds = useMemo(() => new Set(members.map((m) => m.userId)), [members])

  const addMember = () => {
    if (!picked) return
    add.mutate(
      { userId: picked, role },
      {
        onSuccess: (member) => {
          message.success(`${memberDisplayName(member)} ditambahkan.`)
          setPicked(null)
        },
        onError: fail,
      },
    )
  }

  const columns = useMemo<TableColumnsType<ProjectMember>>(() => {
    const base: TableColumnsType<ProjectMember> = [
      {
        title: 'Nama',
        key: 'name',
        render: (_, m) => (
          <>
            {memberDisplayName(m)}
            {m.isActive === false && <Tag style={{ marginLeft: 8 }}>Nonaktif</Tag>}
          </>
        ),
      },
      { title: 'Email', dataIndex: 'email', render: (value: string | null) => value ?? '-' },
      {
        title: 'Peran',
        dataIndex: 'role',
        render: (value: ProjectRole, m) =>
          editable ? (
            <MemberRoleSelect projectId={projectId} member={m} />
          ) : (
            <Tag color={value === 'manager' ? 'gold' : undefined}>{projectRoleLabels[value]}</Tag>
          ),
      },
      { title: 'Bergabung', dataIndex: 'joinedAt', render: (value: string) => value.slice(0, 10) },
    ]
    if (editable)
      base.push({
        title: 'Aksi',
        key: 'actions',
        render: (_, m) => <RemoveMemberButton projectId={projectId} member={m} />,
      })
    return base
  }, [editable, projectId])

  return (
    <>
      {editable && (
        <Flex gap={8} wrap align="center" style={{ marginBottom: 16 }}>
          <MemberPicker
            memberIds={memberIds}
            value={picked}
            onChange={setPicked}
            disabled={add.isPending}
          />
          <Select<ProjectRole>
            aria-label="Peran anggota baru"
            style={{ width: 130 }}
            value={role}
            options={roleOptions}
            onChange={setRole}
          />
          <Button type="primary" onClick={addMember} disabled={!picked} loading={add.isPending}>
            Tambah anggota
          </Button>
        </Flex>
      )}
      {!canManage && !manageUnknown && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          message="Hanya manager proyek yang bisa mengubah anggota."
        />
      )}
      <Table<ProjectMember>
        rowKey="userId"
        columns={columns}
        dataSource={members}
        loading={loading}
        pagination={{
          pageSize: 20,
          hideOnSinglePage: true,
          showTotal: (total) => `${total} anggota`,
        }}
        size="middle"
        scroll={{ x: true }}
      />
    </>
  )
}
