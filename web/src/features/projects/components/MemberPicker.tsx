import { Select, Spin } from 'antd'
import { useState } from 'react'
import { useUsers } from '@/features/users/hooks/useUsers'
import { useDebouncedValue } from '@/shared/hooks/useDebouncedValue'
import { MAX_PAGE_SIZE } from '@/shared/types/paging'

interface MemberPickerProps {
  /** User yang sudah menjadi anggota tampil tetapi tidak bisa dipilih. */
  memberIds: ReadonlySet<string>
  value: string | null
  onChange: (userId: string | null) => void
  disabled?: boolean
}

/**
 * Mencari user aktif tenant ini (butuh `user.read`) untuk dijadikan anggota proyek. Anggota yang sudah ada ditandai,
 * bukan disembunyikan, agar jelas mengapa user itu tidak bisa dipilih; hasil pencarian dimuat sampai batas halaman.
 */
export function MemberPicker({ memberIds, value, onChange, disabled }: MemberPickerProps) {
  const [text, setText] = useState('')
  const search = useDebouncedValue(text.trim())
  const users = useUsers({
    search: search || undefined,
    status: 'active',
    page: 1,
    size: MAX_PAGE_SIZE,
  })

  const options = (users.data?.items ?? []).map((u) => ({
    value: u.id,
    label: memberIds.has(u.id)
      ? `${u.fullName} (${u.email}) · sudah anggota`
      : `${u.fullName} (${u.email})`,
    disabled: memberIds.has(u.id),
  }))
  const more = (users.data?.totalCount ?? 0) > (users.data?.items.length ?? 0)

  return (
    <Select
      aria-label="Pilih user"
      showSearch
      allowClear
      filterOption={false}
      placeholder="Cari nama atau email"
      style={{ minWidth: 300 }}
      disabled={disabled}
      value={value ?? undefined}
      onSearch={setText}
      onChange={(userId?: string) => onChange(userId ?? null)}
      notFoundContent={users.isFetching ? <Spin size="small" /> : 'Tidak ada user yang cocok'}
      options={options}
      popupRender={(menu) => (
        <>
          {menu}
          {more && (
            <div style={{ padding: '4px 12px', opacity: 0.65 }}>
              Ketik untuk mempersempit pencarian.
            </div>
          )}
        </>
      )}
    />
  )
}
