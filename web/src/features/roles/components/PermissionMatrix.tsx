import { Checkbox, Table, Tag, Typography } from 'antd'
import type { TableColumnsType } from 'antd'
import type { ReactNode } from 'react'
import type { Permission, Role } from '@/features/roles/api/rolesApi'

interface PermissionMatrixProps {
  roles: Role[]
  /** Katalog permission; baris dikelompokkan per modul. */
  permissions: Permission[]
  /** Permission tiap role yang ditampilkan (termasuk perubahan yang belum disimpan). */
  selected: (role: Role) => ReadonlySet<string>
  /** Role sistem selalu read-only; `false` membuat seluruh matriks read-only. */
  editable: boolean
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
  return sorted.map((permission, index) => ({
    ...permission,
    moduleSpan:
      index > 0 && sorted[index - 1]?.module === permission.module
        ? 0
        : sorted.filter((p) => p.module === permission.module).length,
  }))
}

/** Matriks permission: baris permission per modul, kolom role, checkbox di tiap sel. */
export function PermissionMatrix({
  roles,
  permissions,
  selected,
  editable,
  onToggle,
  renderRoleActions,
}: PermissionMatrixProps) {
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
      const current = selected(role)
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
            checked={current.has(row.code)}
            disabled={!editable || role.isSystem}
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
      dataSource={toRows(permissions)}
      pagination={false}
      bordered
      size="small"
      scroll={{ x: 'max-content' }}
    />
  )
}
