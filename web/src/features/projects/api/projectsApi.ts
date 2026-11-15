import { httpClient } from '@/shared/api/httpClient'
import { ifMatch } from '@/shared/api/ifMatch'
import type { PagedResult } from '@/shared/types/paging'

export type ProjectStatus = 'planning' | 'active' | 'on_hold' | 'completed' | 'archived'
export type ProjectPriority = 'low' | 'medium' | 'high' | 'urgent'

/** Status yang bisa dipilih di form; `archived` hanya lewat aksi arsip. */
export type EditableProjectStatus = Exclude<ProjectStatus, 'archived'>

export interface Project {
  id: string
  code: string
  name: string
  description: string | null
  status: ProjectStatus
  priority: ProjectPriority
  /** Tanggal `YYYY-MM-DD`. */
  startDate: string | null
  endDate: string | null
  progressPct: number
  ownerId: string
  departmentId: string | null
  /** Dikirim balik lewat header `If-Match` saat mengubah data. */
  rowVersion: number
  createdAt: string
}

export interface ProjectListParams {
  search?: string
  /** Tanpa status, proyek yang diarsipkan tidak ikut. */
  status?: ProjectStatus
  ownerId?: string
  departmentId?: string
  page: number
  size: number
}

export interface CreateProjectRequest {
  code: string
  name: string
  description: string | null
  priority: ProjectPriority
  startDate: string | null
  endDate: string | null
  departmentId: string | null
}

export type UpdateProjectRequest = Omit<CreateProjectRequest, 'code'> & {
  status: EditableProjectStatus
}

export const projectsApi = {
  list: (params: ProjectListParams) =>
    httpClient.get<PagedResult<Project>>('/projects', { params }).then((r) => r.data),

  create: (request: CreateProjectRequest) =>
    httpClient.post<Project>('/projects', request).then((r) => r.data),

  update: (id: string, rowVersion: number, request: UpdateProjectRequest) =>
    httpClient.put<Project>(`/projects/${id}`, request, ifMatch(rowVersion)).then((r) => r.data),

  /** Mengarsipkan proyek (status `archived`). */
  archive: (id: string, rowVersion: number) =>
    httpClient.delete<void>(`/projects/${id}`, ifMatch(rowVersion)).then(() => undefined),
}

export const projectKeys = {
  all: ['projects'] as const,
  lists: () => [...projectKeys.all, 'list'] as const,
  list: (params: ProjectListParams) => [...projectKeys.lists(), params] as const,
}

/** Pesan konflik versi (409); daftar dan form sudah memuat data terbaru saat pesan ini tampil. */
export const CONFLICT_MESSAGE =
  'Data telah diubah orang lain. Data terbaru sudah dimuat; periksa lalu simpan lagi.'

export const projectStatusLabels: Record<ProjectStatus, string> = {
  planning: 'Perencanaan',
  active: 'Aktif',
  on_hold: 'Ditunda',
  completed: 'Selesai',
  archived: 'Diarsipkan',
}

export const projectPriorityLabels: Record<ProjectPriority, string> = {
  low: 'Rendah',
  medium: 'Sedang',
  high: 'Tinggi',
  urgent: 'Mendesak',
}
