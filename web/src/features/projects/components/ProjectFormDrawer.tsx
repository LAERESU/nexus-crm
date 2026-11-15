import { Alert, App, Drawer } from 'antd'
import { useState } from 'react'
import { CONFLICT_MESSAGE } from '@/features/projects/api/projectsApi'
import type { Project } from '@/features/projects/api/projectsApi'
import { ProjectForm } from '@/features/projects/components/ProjectForm'
import type { ProjectFormValues } from '@/features/projects/components/ProjectForm'
import { useCreateProject, useUpdateProject } from '@/features/projects/hooks/useProjects'
import type { Department } from '@/features/users/api/usersApi'
import { parseProblem } from '@/shared/api/problemDetails'

interface ProjectFormDrawerProps {
  open: boolean
  /** Proyek yang diubah (baris terbaru dari daftar); null berarti membuat proyek baru. */
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
  status: p.status === 'archived' ? 'completed' : p.status,
  priority: p.priority,
  startDate: p.startDate ?? '',
  endDate: p.endDate ?? '',
  departmentId: p.departmentId,
})

/** Drawer buat/ubah proyek. Konflik 409: daftar dimuat ulang dan drawer memakai versi terbaru. */
export function ProjectFormDrawer({ open, project, departments, onClose }: ProjectFormDrawerProps) {
  const { message } = App.useApp()
  const create = useCreateProject()
  const update = useUpdateProject()
  const [conflict, setConflict] = useState(false)
  // Selama menyimpan drawer tidak bisa ditutup, agar hasil yang datang belakangan tidak menutup drawer lain.
  const closable = !create.isPending && !update.isPending

  // Departemen proyek tetap bisa ditampilkan walau tidak termasuk daftar pilihan yang dimuat.
  const options =
    project?.departmentId && !departments.some((d) => d.id === project.departmentId)
      ? [
          ...departments,
          { id: project.departmentId, code: '', name: 'Departemen saat ini', parentId: null },
        ]
      : departments

  const submit = async (values: ProjectFormValues) => {
    const common = {
      name: values.name,
      description: values.description || null,
      priority: values.priority,
      startDate: values.startDate || null,
      endDate: values.endDate || null,
      departmentId: values.departmentId,
    }
    setConflict(false)
    if (project) {
      try {
        await update.mutateAsync({ project, request: { ...common, status: values.status } })
      } catch (e) {
        // 409: daftar sudah dimuat ulang (onSettled); form dimuat ulang dengan data terbaru (key berganti versi).
        if (parseProblem(e).status === 409) {
          setConflict(true)
          return
        }
        throw e // error lain ditampilkan form
      }
      message.success('Perubahan proyek disimpan.')
    } else {
      const created = await create.mutateAsync({ ...common, code: values.code })
      message.success(`Proyek ${created.code} dibuat.`)
    }
    close()
  }

  const close = () => {
    setConflict(false)
    onClose()
  }

  return (
    <Drawer
      title={project ? `Ubah proyek ${project.code}` : 'Proyek baru'}
      open={open}
      onClose={close}
      closable={closable}
      maskClosable={closable}
      keyboard={closable}
      destroyOnHidden
      width={480}
    >
      {conflict && (
        <Alert
          type="warning"
          showIcon
          role="alert"
          style={{ marginBottom: 16 }}
          message={CONFLICT_MESSAGE}
        />
      )}
      {open && (
        <ProjectForm
          key={project ? `${project.id}:${project.rowVersion}` : 'baru'}
          initialValues={project ? toForm(project) : emptyForm}
          editing={project !== null}
          departments={options}
          submitLabel={project ? 'Simpan' : 'Buat proyek'}
          onSubmit={submit}
          onCancel={close}
        />
      )}
    </Drawer>
  )
}
