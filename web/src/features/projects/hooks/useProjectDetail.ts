import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { projectDetailApi, projectKeys } from '@/features/projects/api/projectsApi'
import type {
  Milestone,
  MilestoneRequest,
  MilestoneStatus,
  ProjectRole,
} from '@/features/projects/api/projectsApi'
import { parseProblem } from '@/shared/api/problemDetails'

/** 403/404 tidak diulang: jawabannya tidak akan berubah. */
const retryUnlessDenied = (failureCount: number, error: unknown) => {
  const status = parseProblem(error).status
  return status !== 403 && status !== 404 && failureCount < 1
}

export function useProject(id: string) {
  return useQuery({
    queryKey: projectKeys.detail(id),
    queryFn: () => projectDetailApi.get(id),
    retry: retryUnlessDenied,
  })
}

export function useProjectMembers(id: string, enabled: boolean) {
  return useQuery({
    queryKey: projectKeys.members(id),
    queryFn: () => projectDetailApi.members(id),
    enabled,
    retry: retryUnlessDenied,
  })
}

export function useMilestones(id: string, enabled: boolean) {
  return useQuery({
    queryKey: projectKeys.milestones(id),
    queryFn: () => projectDetailApi.milestones(id),
    enabled,
    retry: retryUnlessDenied,
  })
}

/**
 * Perubahan anggota atau milestone menaikkan versi proyek (ROW_VERSION), jadi detail, anggota, milestone, dan daftar
 * proyek dimuat ulang. Mengembalikan promise pemuatan ulang detail (anggota dan milestone ikut, karena kuncinya di
 * bawah kunci detail), agar pemanggil bisa menunggunya sebelum memberi tahu "data terbaru sudah dimuat".
 */
export function useReloadProject(id: string) {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: projectKeys.lists() })
    return queryClient.invalidateQueries({ queryKey: projectKeys.detail(id) })
  }
}

// Setiap baris memakai instance mutasinya sendiri (komponen per baris), sehingga callback dan status memuat dua aksi
// yang tumpang tindih tidak saling menimpa.

export function useAddMember(id: string) {
  const reload = useReloadProject(id)
  return useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: ProjectRole }) =>
      projectDetailApi.addMember(id, userId, role),
    onSettled: () => void reload(),
  })
}

export function useChangeMemberRole(id: string) {
  const reload = useReloadProject(id)
  return useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: ProjectRole }) =>
      projectDetailApi.changeMemberRole(id, userId, role),
    onSettled: () => void reload(),
  })
}

export function useRemoveMember(id: string) {
  const reload = useReloadProject(id)
  return useMutation({
    mutationFn: (userId: string) => projectDetailApi.removeMember(id, userId),
    onSettled: () => void reload(),
  })
}

export function useCreateMilestone(id: string) {
  const reload = useReloadProject(id)
  return useMutation({
    mutationFn: (request: MilestoneRequest) => projectDetailApi.createMilestone(id, request),
    onSettled: () => void reload(),
  })
}

/** Pemuatan ulang ditunggu: pada 409, pemanggil memberi tahu pengguna setelah data terbaru benar-benar tiba. */
export function useUpdateMilestone(id: string) {
  const reload = useReloadProject(id)
  return useMutation({
    mutationFn: ({ milestone, status }: { milestone: Milestone; status: MilestoneStatus }) =>
      projectDetailApi.updateMilestone(milestone, {
        name: milestone.name,
        description: milestone.description,
        dueDate: milestone.dueDate,
        status,
      }),
    onSettled: () => reload(),
  })
}
