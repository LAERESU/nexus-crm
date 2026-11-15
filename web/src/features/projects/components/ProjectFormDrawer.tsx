import { Alert, App, Drawer } from 'antd'
import { useState } from 'react'
import { CONFLICT_MESSAGE } from '@/features/projects/api/projectsApi'
import type { Project } from '@/features/projects/api/projectsApi'
import { ProjectForm } from '@/features/projects/components/ProjectForm'
import type { ProjectFormValues } from '@/features/projects/components/ProjectForm'
import { useCreateProject, useUpdateProject } from '@/features/projects/hooks/useProjects'
import { withCurrentDepartment } from '@/features/users/api/departmentOptions'
import type { Department } from '@/features/users/api/usersApi'
import { parseProblem } from '@/shared/api/problemDetails'

interface ProjectFormDrawerProps {
  /** Proyek yang diubah (baris terbaru dari daftar, atau snapshot bila tidak lagi tampil); null berarti proyek baru. */
  project: Project | null
  departments: Department[]
  onClose: () => void
}

const emptyForm: ProjectFormValues = {
  code: '',
  name: '',
  description: '',
  status: 'planning',
  priority: 'medium',
  startDate: '',
  endDate: '',
  departmentId: null,
}

const toForm = (p: Project): ProjectFormValues => ({
  code: p.code,
  name: p.name,
  description: p.description ?? '',
  // Baris arsip tidak punya tombol ubah; pemetaan ini hanya untuk tipe.
  status: p.status === 'archived' ? 'completed' : p.status,
  priority: p.priority,
  startDate: p.startDate ?? '',
  endDate: p.endDate ?? '',
  departmentId: p.departmentId,
})

/**
 * Drawer buat/ubah proyek; dipasang ulang setiap kali dibuka, sehingga statusnya tidak terbawa ke pembukaan berikutnya.
 *
 * Form disimpan dengan versi data yang ditampilkannya (bukan versi terbaru di daftar), agar perubahan orang lain yang
 * belum dilihat pengguna tidak tertimpa diam-diam: server menjawab 409. Setelah 409 daftar dimuat ulang dan form
 * dimuat ulang dengan versi terbaru; pemuatan ulang daftar di latar belakang tanpa konflik tidak mengganti isian.
 */
export function ProjectFormDrawer({ project, departments, onClose }: ProjectFormDrawerProps) {
  const { message } = App.useApp()
  const create = useCreateProject()
  const update = useUpdateProject()
  const [openedVersion] = useState(() => project?.rowVersion ?? 0)
  const [conflictAt, setConflictAt] = useState<number | null>(null)
  const closable = !create.isPending && !update.isPending

  // Versi yang sedang ditampilkan form: versi saat dibuka, atau versi terbaru setelah konflik.
  const loadedVersion = project ? (conflictAt === null ? openedVersion : project.rowVersion) : 0
  const reloaded = conflictAt !== null && loadedVersion > conflictAt

  const submit = async (values: ProjectFormValues) => {
    const common = {
      name: values.name,
      description: values.description || null,
      priority: values.priority,
      startDate: values.startDate || null,
      endDate: values.endDate || null,
      departmentId: values.departmentId,
    }
    if (project) {
      try {
        await update.mutateAsync({
          project: { ...project, rowVersion: loadedVersion },
          request: { ...common, status: values.status },
        })
      } catch (e) {
        if (parseProblem(e).status === 409) {
          setConflictAt(loadedVersion) // daftar dimuat ulang (onSettled); form dimuat ulang begitu versi baru tiba
          return
        }
        throw e // error lain ditampilkan form
      }
      message.success('Perubahan proyek disimpan.')
    } else {
      const created = await create.mutateAsync({ ...common, code: values.code })
      message.success(`Proyek ${created.code} dibuat.`)
    }
    onClose()
  }

  return (
    <Drawer
      title={project ? `Ubah proyek ${project.code}` : 'Proyek baru'}
      open
      onClose={onClose}
      closable={closable}
      maskClosable={closable}
      keyboard={closable}
      width={480}
    >
      {conflictAt !== null && (
        <Alert
          type="warning"
          showIcon
          role="alert"
          style={{ marginBottom: 16 }}
          message={
            reloaded ? CONFLICT_MESSAGE : 'Data telah diubah orang lain. Memuat data terbaru…'
          }
        />
      )}
      <ProjectForm
        key={project ? `${project.id}:${loadedVersion}` : 'baru'}
        initialValues={project ? toForm(project) : emptyForm}
        editing={project !== null}
        departments={withCurrentDepartment(departments, project?.departmentId)}
        submitLabel={project ? 'Simpan' : 'Buat proyek'}
        onSubmit={submit}
        onCancel={onClose}
      />
    </Drawer>
  )
}
