import { Select, Spin } from 'antd'
import { useState } from 'react'
import { useUsers } from '@/features/users/hooks/useUsers'
import { useDebouncedValue } from '@/shared/hooks/useDebouncedValue'

interface MemberPickerProps {
  /** User yang sudah menjadi anggota tidak ditawarkan lagi. */
  excludeUserIds: ReadonlySet<string>
  value: string | null
  onChange: (userId: string | null) => void
  disabled?: boolean
}

/** Mencari user aktif tenant ini (butuh `user.read`) untuk dijadikan anggota proyek. */
export function MemberPicker({ excludeUserIds, value, onChange, disabled }: MemberPickerProps) {
  const [text, setText] = useState('')
  const search = useDebouncedValue(text.trim())
  const users = useUsers({ search: search || undefined, status: 'active', page: 1, size: 20 })

  const options = (users.data?.items ?? [])
    .filter((u) => !excludeUserIds.has(u.id))
    .map((u) => ({ value: u.id, label: `${u.fullName} (${u.email})` }))

  return (
    <Select
      aria-label="Pilih user"
      showSearch
      allowClear
      filterOption={false}
      placeholder="Cari nama atau email"
      style={{ minWidth: 280 }}
      disabled={disabled}
      value={value ?? undefined}
      onSearch={setText}
      onChange={(userId?: string) => onChange(userId ?? null)}
      notFoundContent={users.isFetching ? <Spin size="small" /> : 'Tidak ada user yang cocok'}
      options={options}
    />
  )
}
