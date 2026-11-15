import { Alert, App, Button, Flex, Popconfirm, Select, Table, Tag } from 'antd'
import type { TableColumnsType } from 'antd'
import { useState } from 'react'
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
  /** Proyek yang diarsipkan tidak bisa diubah anggotanya. */
  readOnly: boolean
}

const roleOptions = projectRoles.map((value) => ({ value, label: projectRoleLabels[value] }))

export function ProjectMembersTab({
  projectId,
  members,
  loading,
  canManage,
  readOnly,
}: ProjectMembersTabProps) {
  const { message } = App.useApp()
  const add = useAddMember(projectId)
  const changeRole = useChangeMemberRole(projectId)
  const remove = useRemoveMember(projectId)
  const [picked, setPicked] = useState<string | null>(null)
  const [role, setRole] = useState<ProjectRole>('member')
  const editable = canManage && !readOnly

  const fail = (error: unknown) => message.error(problemMessage(parseProblem(error)))

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

  const columns: TableColumnsType<ProjectMember> = [
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
          <Select<ProjectRole>
            aria-label={`Peran ${memberDisplayName(m)}`}
            size="small"
            style={{ width: 130 }}
            value={value}
            options={roleOptions}
            disabled={changeRole.isPending}
            onChange={(next) =>
              changeRole.mutate(
                { userId: m.userId, role: next },
                {
                  onSuccess: () => message.success(`Peran ${memberDisplayName(m)} diubah.`),
                  onError: fail,
                },
              )
            }
          />
        ) : (
          <Tag color={value === 'manager' ? 'gold' : undefined}>{projectRoleLabels[value]}</Tag>
        ),
    },
    { title: 'Bergabung', dataIndex: 'joinedAt', render: (value: string) => value.slice(0, 10) },
  ]
  if (editable)
    columns.push({
      title: 'Aksi',
      key: 'actions',
      render: (_, m) => (
        <Popconfirm
          title={`Keluarkan ${memberDisplayName(m)} dari proyek?`}
          okText="Keluarkan"
          okButtonProps={{ danger: true }}
          cancelText="Batal"
          onConfirm={() =>
            remove.mutate(m.userId, {
              onSuccess: () => message.success(`${memberDisplayName(m)} dikeluarkan.`),
              onError: fail,
            })
          }
        >
          <Button size="small" danger loading={remove.isPending && remove.variables === m.userId}>
            Keluarkan
          </Button>
        </Popconfirm>
      ),
    })

  return (
    <>
      {editable && (
        <Flex gap={8} wrap align="center" style={{ marginBottom: 16 }}>
          <MemberPicker
            excludeUserIds={new Set(members.map((m) => m.userId))}
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
      {!canManage && (
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
        pagination={false}
        size="middle"
        scroll={{ x: true }}
      />
    </>
  )
}
