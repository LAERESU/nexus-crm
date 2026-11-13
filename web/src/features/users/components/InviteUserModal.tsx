import { App, Modal } from 'antd'
import type { Department } from '@/features/users/api/usersApi'
import { UserForm } from '@/features/users/components/UserForm'
import type { UserFormValues } from '@/features/users/components/UserForm'
import { useInviteUser } from '@/features/users/hooks/useUsers'

const emptyUserForm: UserFormValues = { email: '', fullName: '', jobTitle: '', departmentId: null }

interface InviteUserModalProps {
  open: boolean
  departments: Department[]
  onClose: () => void
}

export function InviteUserModal({ open, departments, onClose }: InviteUserModalProps) {
  const { message } = App.useApp()
  const invite = useInviteUser()

  const submit = async (values: UserFormValues) => {
    const user = await invite.mutateAsync({
      email: values.email,
      fullName: values.fullName,
      jobTitle: values.jobTitle || null,
      departmentId: values.departmentId,
    })
    message.success(`Undangan dikirim ke ${user.email}.`)
    onClose()
  }

  return (
    <Modal title="Undang user" open={open} onCancel={onClose} footer={null} destroyOnHidden>
      <UserForm
        initialValues={emptyUserForm}
        departments={departments}
        submitLabel="Kirim undangan"
        onSubmit={submit}
        onCancel={onClose}
      />
    </Modal>
  )
}
