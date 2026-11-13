import { Checkbox, Table, Tag, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import { useMemo } from 'react'
import type { ReactNode } from 'react'
import type { Permission, Role } from '@/features/roles/api/rolesApi'

interface PermissionMatrixProps {
  roles: Role[]
  /** Katalog permission; baris dikelompokkan per modul. */
  permissions: Permission[]
  /** Permission tiap role yang ditampilkan (termasuk perubahan yang belum disimpan). */
  selected: ReadonlyMap<string, ReadonlySet<string>>
  /** Role sistem selalu read-only; `false` membuat seluruh matriks read-only. */
  editable: boolean
  /** Role yang sedang disimpan: kolomnya dikunci sementara. */
  lockedRoleId?: string | null
  onToggle: (role: Role, permission: string, checked: boolean) => void
  /** Isi tambahan di kepala kolom role (mis. tombol simpan). */
  renderRoleActions?: (role: Role) => ReactNode
}

interface Row extends Permission {
  /** Jumlah baris modul ini bila baris pertama modul, selain itu 0 (sel modul digabung). */
  moduleSpan: number
}

function toRows(permissions: Permission[]): Row[] {
  const sorted = [...permissions].sort(
    (a, b) => a.module.localeCompare(b.module) || a.code.localeCompare(b.code),
  )
  const perModule = new Map<string, number>()
  for (const { module } of sorted) perModule.set(module, (perModule.get(module) ?? 0) + 1)
  return sorted.map((permission, index) => ({
    ...permission,
    moduleSpan:
      sorted[index - 1]?.module === permission.module ? 0 : (perModule.get(permission.module) ?? 1),
  }))
}

/** Matriks permission: baris permission per modul, kolom role, checkbox di tiap sel. */
export function PermissionMatrix({
  roles,
  permissions,
  selected,
  editable,
  lockedRoleId = null,
  onToggle,
  renderRoleActions,
}: PermissionMatrixProps) {
  const rows = useMemo(() => toRows(permissions), [permissions])
  const columns: TableColumnsType<Row> = [
    {
      title: 'Modul',
      dataIndex: 'module',
      fixed: 'left',
      width: 120,
      onCell: (row) => ({ rowSpan: row.moduleSpan }),
      render: (module: string) => <Typography.Text strong>{module}</Typography.Text>,
    },
    {
      title: 'Permission',
      dataIndex: 'code',
      fixed: 'left',
      width: 260,
      render: (code: string, row) => (
        <>
          <Typography.Text code>{code}</Typography.Text>
          {row.description && (
            <div>
              <Typography.Text type="secondary">{row.description}</Typography.Text>
            </div>
          )}
        </>
      ),
    },
    ...roles.map((role) => {
      const current = selected.get(role.id)
      return {
        key: role.id,
        align: 'center' as const,
        width: 150,
        title: (
          <>
            <div>{role.name}</div>
            {role.isSystem && <Tag>Sistem</Tag>}
            {renderRoleActions?.(role)}
          </>
        ),
        render: (_: unknown, row: Row) => (
          <Checkbox
            aria-label={`${role.name}: ${row.code}`}
            checked={current?.has(row.code) ?? false}
            disabled={!editable || role.isSystem || role.id === lockedRoleId}
            onChange={(e) => onToggle(role, row.code, e.target.checked)}
          />
        ),
      }
    }),
  ]

  return (
    <Table<Row>
      rowKey="code"
      columns={columns}
      dataSource={rows}
      pagination={false}
      bordered
      size="small"
      scroll={{ x: 'max-content' }}
    />
  )
}
