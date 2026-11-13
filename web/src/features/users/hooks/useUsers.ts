import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { userKeys, usersApi } from '@/features/users/api/usersApi'
import type {
  InviteUserRequest,
  UpdateUserRequest,
  User,
  UserListParams,
  UserStatus,
} from '@/features/users/api/usersApi'

export function useUsers(params: UserListParams) {
  return useQuery({
    queryKey: userKeys.list(params),
    queryFn: () => usersApi.list(params),
    // Baris lama tetap tampil selama halaman/filter baru dimuat.
    placeholderData: keepPreviousData,
  })
}

export function useDepartments() {
  return useQuery({
    queryKey: userKeys.departments(),
    queryFn: usersApi.departments,
    staleTime: 5 * 60_000,
  })
}

function useInvalidateUserLists() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: userKeys.lists() })
}

export function useInviteUser() {
  const invalidate = useInvalidateUserLists()
  return useMutation({
    mutationFn: (request: InviteUserRequest) => usersApi.invite(request),
    onSuccess: invalidate,
  })
}

/** Daftar dimuat ulang juga saat gagal: pada 409 baris yang tampil sudah usang. */
export function useUpdateUser() {
  const invalidate = useInvalidateUserLists()
  return useMutation({
    mutationFn: ({ user, request }: { user: User; request: UpdateUserRequest }) =>
      usersApi.update(user.id, user.rowVersion, request),
    onSettled: invalidate,
  })
}

export function useSetUserStatus() {
  const invalidate = useInvalidateUserLists()
  return useMutation({
    mutationFn: ({ user, status }: { user: User; status: UserStatus }) =>
      usersApi.setStatus(user.id, user.rowVersion, status),
    onSettled: invalidate,
  })
}
