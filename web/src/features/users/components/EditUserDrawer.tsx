import { App, Drawer } from 'antd'
import type { Department, User } from '@/features/users/api/usersApi'
import { UserForm } from '@/features/users/components/UserForm'
import type { UserFormValues } from '@/features/users/components/UserForm'
import { useUpdateUser } from '@/features/users/hooks/useUsers'

interface EditUserDrawerProps {
  /** User yang diedit; `null` menutup drawer. */
  user: User | null
  departments: Department[]
  onClose: () => void
}

export function EditUserDrawer({ user, departments, onClose }: EditUserDrawerProps) {
  const { message } = App.useApp()
  const update = useUpdateUser()

  const submit = async (values: UserFormValues) => {
    if (!user) return
    await update.mutateAsync({
      user,
      request: {
        fullName: values.fullName,
        jobTitle: values.jobTitle || null,
        departmentId: values.departmentId,
      },
    })
    message.success('Perubahan disimpan.')
    onClose()
  }

  return (
    <Drawer title="Edit user" open={user !== null} onClose={onClose} destroyOnHidden>
      {user && (
        <UserForm
          key={user.id}
          initialValues={{
            email: user.email,
            fullName: user.fullName,
            jobTitle: user.jobTitle ?? '',
            departmentId: user.departmentId,
          }}
          emailReadOnly
          departments={departments}
          submitLabel="Simpan"
          onSubmit={submit}
          onCancel={onClose}
        />
      )}
    </Drawer>
  )
}
