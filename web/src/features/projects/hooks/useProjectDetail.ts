import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { projectDetailApi, projectKeys } from '@/features/projects/api/projectsApi'
import type {
  Milestone,
  MilestoneRequest,
  MilestoneStatus,
  ProjectRole,
} from '@/features/projects/api/projectsApi'

/** 403/404 tidak diulang: jawabannya tidak akan berubah. */
const retryUnlessDenied = (failureCount: number, error: unknown) =>
  !(error instanceof AxiosError && [403, 404].includes(error.response?.status ?? 0)) &&
  failureCount < 1

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
 * proyek dimuat ulang. Tidak ditunggu: mutasi selesai begitu server menjawab.
 */
function useInvalidateProject(id: string) {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: projectKeys.detail(id) })
    void queryClient.invalidateQueries({ queryKey: projectKeys.lists() })
  }
}

export function useAddMember(id: string) {
  const invalidate = useInvalidateProject(id)
  return useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: ProjectRole }) =>
      projectDetailApi.addMember(id, userId, role),
    onSettled: invalidate,
  })
}

export function useChangeMemberRole(id: string) {
  const invalidate = useInvalidateProject(id)
  return useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: ProjectRole }) =>
      projectDetailApi.changeMemberRole(id, userId, role),
    onSettled: invalidate,
  })
}

export function useRemoveMember(id: string) {
  const invalidate = useInvalidateProject(id)
  return useMutation({
    mutationFn: (userId: string) => projectDetailApi.removeMember(id, userId),
    onSettled: invalidate,
  })
}

export function useCreateMilestone(id: string) {
  const invalidate = useInvalidateProject(id)
  return useMutation({
    mutationFn: (request: MilestoneRequest) => projectDetailApi.createMilestone(id, request),
    onSettled: invalidate,
  })
}

/** Daftar dimuat ulang juga saat gagal: pada 409 milestone yang tampil sudah usang. */
export function useUpdateMilestone(id: string) {
  const invalidate = useInvalidateProject(id)
  return useMutation({
    mutationFn: ({ milestone, status }: { milestone: Milestone; status: MilestoneStatus }) =>
      projectDetailApi.updateMilestone(milestone, {
        name: milestone.name,
        description: milestone.description,
        dueDate: milestone.dueDate,
        status,
      }),
    onSettled: invalidate,
  })
}
