import { Alert, App, Button, Space, Spin, Typography } from 'antd'
import { useState } from 'react'
import { usePermission } from '@/features/auth/hooks/usePermission'
import type { Role } from '@/features/roles/api/rolesApi'
import { PermissionMatrix } from '@/features/roles/components/PermissionMatrix'
import {
  usePermissionCatalog,
  useRoles,
  useSetRolePermissions,
} from '@/features/roles/hooks/useRoles'
import { parseProblem, problemMessage } from '@/shared/api/problemDetails'

/** Perubahan satu role yang belum disimpan, beserta versi role saat perubahan mulai dibuat. */
interface Draft {
  baseRowVersion: number
  permissions: string[]
}

const sameSet = (a: readonly string[], b: readonly string[]) =>
  a.length === b.length && a.every((code) => b.includes(code))

export default function RoleManagementPage() {
  const { message } = App.useApp()
  const canManage = usePermission('role.manage')
  const roles = useRoles()
  const catalog = usePermissionCatalog()
  const save = useSetRolePermissions()
  const [drafts, setDrafts] = useState<Record<string, Draft>>({})

  const dropDraft = (roleId: string) =>
    setDrafts((current) => {
      const next = { ...current }
      delete next[roleId]
      return next
    })

  const toggle = (role: Role, permission: string, checked: boolean) =>
    setDrafts((current) => {
      const base = current[role.id]?.permissions ?? role.permissions
      const permissions = checked
        ? [...base.filter((code) => code !== permission), permission]
        : base.filter((code) => code !== permission)
      const next = { ...current }
      // Kembali sama dengan yang tersimpan: tidak ada lagi yang perlu disimpan.
      if (sameSet(permissions, role.permissions)) delete next[role.id]
      else
        next[role.id] = {
          baseRowVersion: current[role.id]?.baseRowVersion ?? role.rowVersion,
          permissions,
        }
      return next
    })

  const saveRole = (role: Role) => {
    const draft = drafts[role.id]
    if (!draft) return
    save.mutate(
      { roleId: role.id, rowVersion: draft.baseRowVersion, permissions: draft.permissions },
      {
        onSuccess: () => {
          dropDraft(role.id)
          message.success(`Permission role ${role.name} disimpan.`)
        },
        onError: (error) => {
          const problem = parseProblem(error)
          // Role sudah diubah pengguna lain: perubahan lokal dibuang agar tidak menimpa perubahan itu.
          if (problem.status === 409) {
            dropDraft(role.id)
            message.error(
              `Role ${role.name} sudah diubah pengguna lain. Perubahan Anda dibatalkan; periksa lalu ulangi.`,
            )
          } else message.error(problemMessage(problem))
        },
      },
    )
  }

  const error = roles.error ?? catalog.error
  const items = roles.data?.items ?? []
  const saving = (role: Role) => save.isPending && save.variables.roleId === role.id

  return (
    <>
      <Typography.Title level={3}>Role & permission</Typography.Title>
      <Typography.Paragraph type="secondary">
        Centang permission untuk tiap role, lalu simpan per role. Role sistem tidak bisa diubah.
      </Typography.Paragraph>

      {error && (
        <Alert
          type="error"
          showIcon
          role="alert"
          style={{ marginBottom: 16 }}
          message={problemMessage(parseProblem(error))}
          action={
            <Button
              size="small"
              onClick={() => {
                void roles.refetch()
                void catalog.refetch()
              }}
            >
              Coba lagi
            </Button>
          }
        />
      )}
      {roles.data && roles.data.totalCount > items.length && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message={`Menampilkan ${items.length} dari ${roles.data.totalCount} role.`}
        />
      )}

      {roles.data && catalog.data ? (
        <PermissionMatrix
          roles={items}
          permissions={catalog.data}
          editable={canManage}
          selected={(role) => new Set(drafts[role.id]?.permissions ?? role.permissions)}
          onToggle={toggle}
          renderRoleActions={(role) =>
            drafts[role.id] && (
              <Space size={4} style={{ marginTop: 4 }}>
                <Button
                  type="primary"
                  size="small"
                  aria-label={`Simpan ${role.name}`}
                  loading={saving(role)}
                  disabled={save.isPending && !saving(role)}
                  onClick={() => saveRole(role)}
                >
                  Simpan
                </Button>
                <Button
                  size="small"
                  aria-label={`Batalkan perubahan ${role.name}`}
                  disabled={save.isPending}
                  onClick={() => dropDraft(role.id)}
                >
                  Batal
                </Button>
              </Space>
            )
          }
        />
      ) : (
        !error && <Spin size="large" aria-label="Memuat role" />
      )}
    </>
  )
}
