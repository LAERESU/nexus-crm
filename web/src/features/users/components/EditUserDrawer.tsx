import { App, Drawer } from 'antd'
import { withCurrentDepartment } from '@/features/users/api/departmentOptions'
import type { Department, User } from '@/features/users/api/usersApi'
import { UserForm } from '@/features/users/components/UserForm'
import type { UserFormValues } from '@/features/users/components/UserForm'
import { useUpdateUser } from '@/features/users/hooks/useUsers'

interface EditUserDrawerProps {
  /** User yang diedit (baris terbaru dari daftar); `null` menutup drawer. */
  user: User | null
  departments: Department[]
  onClose: () => void
}

export function EditUserDrawer({ user, departments, onClose }: EditUserDrawerProps) {
  const { message } = App.useApp()
  const update = useUpdateUser()
  // Selama menyimpan drawer tidak bisa ditutup, agar hasil yang datang belakangan tidak menutup drawer user lain.
  const closable = !update.isPending

  // Departemen user saat ini tetap bisa ditampilkan walau tidak termasuk daftar pilihan yang dimuat.
  const options = withCurrentDepartment(departments, user?.departmentId, user?.departmentName)

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
    <Drawer
      title="Edit user"
      open={user !== null}
      onClose={onClose}
      closable={closable}
      maskClosable={closable}
      keyboard={closable}
      destroyOnHidden
    >
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
          departments={options}
          submitLabel="Simpan"
          onSubmit={submit}
          onCancel={onClose}
        />
      )}
    </Drawer>
  )
}
