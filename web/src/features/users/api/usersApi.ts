import { httpClient } from '@/shared/api/httpClient'
import type { PagedResult } from '@/shared/types/paging'

export type UserStatus = 'active' | 'inactive'

export interface User {
  id: string
  email: string
  fullName: string
  jobTitle: string | null
  status: UserStatus
  departmentId: string | null
  departmentName: string | null
  /** Dikirim balik lewat header `If-Match` saat mengubah data. */
  rowVersion: number
  createdAt: string
}

export interface Department {
  id: string
  code: string
  name: string
  parentId: string | null
}

export interface UserListParams {
  search?: string
  departmentId?: string
  status?: UserStatus
  page: number
  size: number
}

export interface InviteUserRequest {
  email: string
  fullName: string
  jobTitle: string | null
  departmentId: string | null
}

export type UpdateUserRequest = Omit<InviteUserRequest, 'email'>

/** Batas `size` endpoint list (CONTEXT.md bagian 6). */
export const MAX_PAGE_SIZE = 100

const ifMatch = (rowVersion: number) => ({ headers: { 'If-Match': `"${rowVersion}"` } })

export const usersApi = {
  list: (params: UserListParams) =>
    httpClient.get<PagedResult<User>>('/users', { params }).then((r) => r.data),

  /** Membuat user; Auth Service lalu mengirim email undangan (NX-021). */
  invite: (request: InviteUserRequest) =>
    httpClient.post<User>('/users', request).then((r) => r.data),

  update: (id: string, rowVersion: number, request: UpdateUserRequest) =>
    httpClient.put<User>(`/users/${id}`, request, ifMatch(rowVersion)).then((r) => r.data),

  setStatus: (id: string, rowVersion: number, status: UserStatus) =>
    httpClient
      .patch<User>(`/users/${id}/status`, { status }, ifMatch(rowVersion))
      .then((r) => r.data),

  /** Departemen untuk pilihan filter dan form: satu halaman berukuran maksimum. */
  departments: () =>
    httpClient
      .get<PagedResult<Department>>('/departments', { params: { page: 1, size: MAX_PAGE_SIZE } })
      .then((r) => r.data),
}

export const userKeys = {
  all: ['users'] as const,
  lists: () => [...userKeys.all, 'list'] as const,
  list: (params: UserListParams) => [...userKeys.lists(), params] as const,
  departments: () => [...userKeys.all, 'departments'] as const,
}
