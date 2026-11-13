import { App, Button, Popconfirm } from 'antd'
import type { User, UserStatus } from '@/features/users/api/usersApi'
import { useSetUserStatus } from '@/features/users/hooks/useUsers'
import { parseProblem, problemMessage } from '@/shared/api/problemDetails'

/** Tombol aktif/nonaktif satu baris; tiap baris punya mutasi sendiri agar hasil dan status memuatnya tidak tertukar. */
export function UserStatusButton({ user }: { user: User }) {
  const { message } = App.useApp()
  const setStatus = useSetUserStatus()
  const active = user.status === 'active'

  const toggle = () => {
    const status: UserStatus = active ? 'inactive' : 'active'
    setStatus.mutate(
      { user, status },
      {
        onSuccess: () =>
          message.success(
            status === 'active'
              ? `${user.fullName} diaktifkan.`
              : `${user.fullName} dinonaktifkan.`,
          ),
        onError: (error) => message.error(problemMessage(parseProblem(error))),
      },
    )
  }

  return (
    <Popconfirm
      title={active ? `Nonaktifkan ${user.fullName}?` : `Aktifkan ${user.fullName}?`}
      okText="Ya"
      cancelText="Batal"
      onConfirm={toggle}
    >
      <Button size="small" danger={active} loading={setStatus.isPending}>
        {active ? 'Nonaktifkan' : 'Aktifkan'}
      </Button>
    </Popconfirm>
  )
}
