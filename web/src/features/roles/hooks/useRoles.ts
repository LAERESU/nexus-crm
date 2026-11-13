import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { roleKeys, rolesApi } from '@/features/roles/api/rolesApi'
import type { Role } from '@/features/roles/api/rolesApi'
import type { PagedResult } from '@/shared/types/paging'

export function useRoles() {
  return useQuery({ queryKey: roleKeys.list(), queryFn: rolesApi.list })
}

export function usePermissionCatalog() {
  return useQuery({
    queryKey: roleKeys.permissions(),
    queryFn: rolesApi.permissions,
    staleTime: 5 * 60_000,
  })
}

interface SetRolePermissionsInput {
  roleId: string
  rowVersion: number
  permissions: string[]
}

export function useSetRolePermissions() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ roleId, rowVersion, permissions }: SetRolePermissionsInput) =>
      rolesApi.setPermissions(roleId, rowVersion, permissions),
    // Role hasil simpan (dengan rowVersion baru) langsung menggantikan baris di cache.
    onSuccess: (saved) =>
      queryClient.setQueryData<PagedResult<Role>>(
        roleKeys.list(),
        (current) =>
          current && {
            ...current,
            items: current.items.map((r) => (r.id === saved.id ? saved : r)),
          },
      ),
    // Gagal (mis. 409): muat ulang agar matriks menampilkan keadaan server terbaru.
    onError: () => void queryClient.invalidateQueries({ queryKey: roleKeys.list() }),
  })
}
