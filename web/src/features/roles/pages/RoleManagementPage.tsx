import { Alert, App, Button, Modal, Space, Spin, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useBlocker } from 'react-router-dom'
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
  permissions: ReadonlySet<string>
}

const sameSet = (a: ReadonlySet<string>, b: readonly string[]) =>
  a.size === new Set(b).size && b.every((code) => a.has(code))

const noRoles: Role[] = []

export default function RoleManagementPage() {
  const { message } = App.useApp()
  const canManage = usePermission('role.manage')
  const roles = useRoles()
  const catalog = usePermissionCatalog()
  const save = useSetRolePermissions()
  const [storedDrafts, setDrafts] = useState<Record<string, Draft>>({})
  const items = roles.data?.items ?? noRoles

  // Draft yang isinya sudah sama dengan server (mis. admin lain menyimpan perubahan yang sama) bukan lagi perubahan.
  const drafts = useMemo(() => {
    const dirty: Record<string, Draft> = {}
    for (const role of items) {
      const draft = storedDrafts[role.id]
      if (draft && !sameSet(draft.permissions, role.permissions)) dirty[role.id] = draft
    }
    return dirty
  }, [items, storedDrafts])
  const selected = useMemo(() => {
    const byRole = new Map<string, ReadonlySet<string>>()
    for (const role of items)
      byRole.set(role.id, drafts[role.id]?.permissions ?? new Set(role.permissions))
    return byRole
  }, [items, drafts])
  const hasDrafts = Object.keys(drafts).length > 0

  // Perubahan yang belum disimpan tidak hilang diam-diam saat pindah halaman atau menutup tab.
  const blocker = useBlocker(hasDrafts)
  useEffect(() => {
    if (!hasDrafts) return
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [hasDrafts])

  const dropDraft = (roleId: string) =>
    setDrafts((current) => {
      const next = { ...current }
      delete next[roleId]
      return next
    })

  const toggle = (role: Role, permission: string, checked: boolean) =>
    setDrafts((current) => {
      const draft = current[role.id]
      const permissions = new Set(draft?.permissions ?? role.permissions)
      if (checked) permissions.add(permission)
      else permissions.delete(permission)
      return {
        ...current,
        [role.id]: { baseRowVersion: draft?.baseRowVersion ?? role.rowVersion, permissions },
      }
    })

  const saveRole = (role: Role) => {
    const draft = drafts[role.id]
    if (!draft) return
    save.mutate(
      { roleId: role.id, rowVersion: draft.baseRowVersion, permissions: [...draft.permissions] },
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
  const savingRoleId = save.isPending ? save.variables.roleId : null

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
          // Selama satu role disimpan, kolomnya dikunci agar centang susulan tidak hilang saat hasil simpan tiba.
          lockedRoleId={savingRoleId}
          selected={selected}
          onToggle={toggle}
          renderRoleActions={(role) =>
            drafts[role.id] && (
              <Space size={4} style={{ marginTop: 4 }}>
                <Button
                  type="primary"
                  size="small"
                  aria-label={`Simpan ${role.name}`}
                  loading={savingRoleId === role.id}
                  disabled={savingRoleId !== null && savingRoleId !== role.id}
                  onClick={() => saveRole(role)}
                >
                  Simpan
                </Button>
                <Button
                  size="small"
                  aria-label={`Batalkan perubahan ${role.name}`}
                  disabled={savingRoleId !== null}
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

      <Modal
        title="Tinggalkan halaman?"
        open={blocker.state === 'blocked'}
        okText="Tinggalkan"
        okButtonProps={{ danger: true }}
        cancelText="Tetap di sini"
        onOk={() => blocker.proceed?.()}
        onCancel={() => blocker.reset?.()}
      >
        Ada perubahan permission yang belum disimpan dan akan hilang.
      </Modal>
    </>
  )
}
