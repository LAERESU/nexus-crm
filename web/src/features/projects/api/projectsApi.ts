import { httpClient } from '@/shared/api/httpClient'
import { ifMatch } from '@/shared/api/ifMatch'
import { MAX_PAGE_SIZE } from '@/shared/types/paging'
import type { PagedResult } from '@/shared/types/paging'

/** Status yang bisa dipilih di form; `archived` hanya lewat aksi arsip. Satu sumber untuk tipe, skema form, dan pilihan. */
export const editableProjectStatuses = ['planning', 'active', 'on_hold', 'completed'] as const
export const projectStatuses = [...editableProjectStatuses, 'archived'] as const
export const projectPriorities = ['low', 'medium', 'high', 'urgent'] as const

export type EditableProjectStatus = (typeof editableProjectStatuses)[number]
export type ProjectStatus = (typeof projectStatuses)[number]
export type ProjectPriority = (typeof projectPriorities)[number]

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

export type ProjectRole = 'manager' | 'member' | 'viewer'
export const projectRoles: ProjectRole[] = ['manager', 'member', 'viewer']

export const projectRoleLabels: Record<ProjectRole, string> = {
  manager: 'Manager',
  member: 'Anggota',
  viewer: 'Pengamat',
}

export interface ProjectMember {
  userId: string
  /** Dari read model user; null bila user belum dikenal Project Service. */
  fullName: string | null
  email: string | null
  role: ProjectRole
  joinedAt: string
  isActive: boolean | null
}

/** Nama anggota untuk tampilan: nama, email, atau potongan ID bila user belum dikenal Project Service. */
export const memberDisplayName = (m: ProjectMember) =>
  m.fullName ?? m.email ?? `User ${m.userId.slice(0, 8)}`

export type MilestoneStatus = 'open' | 'completed'

export interface Milestone {
  id: string
  projectId: string
  name: string
  description: string | null
  /** Tanggal `YYYY-MM-DD`. */
  dueDate: string
  status: MilestoneStatus
  rowVersion: number
  createdAt: string
}

export interface MilestoneRequest {
  name: string
  description: string | null
  dueDate: string
}

/** Anggota dan milestone satu proyek dimuat sebagai satu halaman berukuran maksimum. */
const ALL = { page: 1, size: MAX_PAGE_SIZE }

export const projectDetailApi = {
  get: (id: string) => httpClient.get<Project>(`/projects/${id}`).then((r) => r.data),

  members: (id: string) =>
    httpClient
      .get<PagedResult<ProjectMember>>(`/projects/${id}/members`, { params: ALL })
      .then((r) => r.data),

  addMember: (id: string, userId: string, role: ProjectRole) =>
    httpClient.post<ProjectMember>(`/projects/${id}/members`, { userId, role }).then((r) => r.data),

  changeMemberRole: (id: string, userId: string, role: ProjectRole) =>
    httpClient
      .put<ProjectMember>(`/projects/${id}/members/${userId}`, { role })
      .then((r) => r.data),

  removeMember: (id: string, userId: string) =>
    httpClient.delete<void>(`/projects/${id}/members/${userId}`).then(() => undefined),

  milestones: (id: string) =>
    httpClient
      .get<PagedResult<Milestone>>(`/projects/${id}/milestones`, { params: ALL })
      .then((r) => r.data),

  createMilestone: (id: string, request: MilestoneRequest) =>
    httpClient.post<Milestone>(`/projects/${id}/milestones`, request).then((r) => r.data),

  updateMilestone: (
    milestone: Milestone,
    request: MilestoneRequest & { status: MilestoneStatus },
  ) =>
    httpClient
      .put<Milestone>(
        `/projects/${milestone.projectId}/milestones/${milestone.id}`,
        request,
        ifMatch(milestone.rowVersion),
      )
      .then((r) => r.data),
}

export const projectKeys = {
  all: ['projects'] as const,
  lists: () => [...projectKeys.all, 'list'] as const,
  list: (params: ProjectListParams) => [...projectKeys.lists(), params] as const,
  detail: (id: string) => [...projectKeys.all, 'detail', id] as const,
  members: (id: string) => [...projectKeys.detail(id), 'members'] as const,
  milestones: (id: string) => [...projectKeys.detail(id), 'milestones'] as const,
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
