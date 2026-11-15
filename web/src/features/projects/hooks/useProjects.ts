import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { projectKeys, projectsApi } from '@/features/projects/api/projectsApi'
import type {
  CreateProjectRequest,
  Project,
  ProjectListParams,
  UpdateProjectRequest,
} from '@/features/projects/api/projectsApi'

export function useProjects(params: ProjectListParams) {
  return useQuery({
    queryKey: projectKeys.list(params),
    queryFn: () => projectsApi.list(params),
    // Baris lama tetap tampil selama halaman/filter baru dimuat.
    placeholderData: keepPreviousData,
  })
}

function useInvalidateProjectLists() {
  const queryClient = useQueryClient()
  // Tidak ditunggu: mutasi selesai begitu server menjawab, daftar dimuat ulang di latar belakang.
  return () => void queryClient.invalidateQueries({ queryKey: projectKeys.lists() })
}

/** Proyek baru langsung muncul di daftar karena semua daftar proyek diinvalidasi. */
export function useCreateProject() {
  const invalidate = useInvalidateProjectLists()
  return useMutation({
    mutationFn: (request: CreateProjectRequest) => projectsApi.create(request),
    onSuccess: invalidate,
  })
}

/** Daftar dimuat ulang juga saat gagal: pada 409 baris yang tampil sudah usang. */
export function useUpdateProject() {
  const invalidate = useInvalidateProjectLists()
  return useMutation({
    mutationFn: ({ project, request }: { project: Project; request: UpdateProjectRequest }) =>
      projectsApi.update(project.id, project.rowVersion, request),
    onSettled: invalidate,
  })
}

export function useArchiveProject() {
  const invalidate = useInvalidateProjectLists()
  return useMutation({
    mutationFn: (project: Project) => projectsApi.archive(project.id, project.rowVersion),
    onSettled: invalidate,
  })
}
