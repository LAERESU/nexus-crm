import { httpClient } from '@/shared/api/httpClient'
import { ifMatch } from '@/shared/api/ifMatch'
import { MAX_PAGE_SIZE } from '@/shared/types/paging'
import type { PagedResult } from '@/shared/types/paging'

export interface Role {
  id: string
  code: string
  name: string
  description: string | null
  /** Role bawaan sistem tidak bisa diubah. */
  isSystem: boolean
  /** Kode permission `modul.aksi`. */
  permissions: string[]
  /** Dikirim balik lewat header `If-Match` saat mengubah data. */
  rowVersion: number
}

export interface Permission {
  code: string
  module: string
  description: string | null
}

export const rolesApi = {
  /** Matriks menampilkan satu halaman role berukuran maksimum. */
  list: () =>
    httpClient
      .get<PagedResult<Role>>('/roles', { params: { page: 1, size: MAX_PAGE_SIZE } })
      .then((r) => r.data),

  /** Katalog seluruh permission yang dikenal sistem. */
  permissions: () => httpClient.get<Permission[]>('/permissions').then((r) => r.data),

  /** Mengganti seluruh permission role; `rowVersion` adalah versi role saat perubahan mulai dibuat. */
  setPermissions: (id: string, rowVersion: number, permissions: string[]) =>
    httpClient
      .put<Role>(`/roles/${id}/permissions`, { permissions }, ifMatch(rowVersion))
      .then((r) => r.data),
}

export const roleKeys = {
  all: ['roles'] as const,
  list: () => [...roleKeys.all, 'list'] as const,
  permissions: () => [...roleKeys.all, 'permissions'] as const,
}
