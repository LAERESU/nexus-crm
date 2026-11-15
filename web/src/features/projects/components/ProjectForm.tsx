import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Button, Flex, Form, Input, Select } from 'antd'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import {
  editableProjectStatuses,
  projectPriorities,
  projectPriorityLabels,
  projectStatusLabels,
} from '@/features/projects/api/projectsApi'
import type { Department } from '@/features/users/api/usersApi'
import { applyServerErrors } from '@/shared/api/problemDetails'

// Batas sama dengan backend (Project.CodeMaxLength dst.).
const projectSchema = z
  .object({
    code: z
      .string()
      .trim()
      .min(1, 'Kode wajib diisi')
      .max(20, 'Kode maksimal 20 karakter')
      .regex(
        /^[A-Za-z][A-Za-z0-9_-]*$/,
        'Kode diawali huruf dan hanya berisi huruf, angka, garis bawah, atau tanda hubung',
      ),
    name: z.string().trim().min(1, 'Nama wajib diisi').max(200, 'Nama maksimal 200 karakter'),
    description: z.string().trim().max(2000, 'Deskripsi maksimal 2000 karakter'),
    status: z.enum(editableProjectStatuses),
    priority: z.enum(projectPriorities),
    startDate: z.string(),
    endDate: z.string(),
    departmentId: z.string().nullable(),
  })
  .refine((v) => !v.startDate || !v.endDate || v.startDate <= v.endDate, {
    path: ['endDate'],
    message: 'Tanggal selesai tidak boleh sebelum tanggal mulai',
  })

export type ProjectFormValues = z.infer<typeof projectSchema>

const fields = [
  'code',
  'name',
  'description',
  'status',
  'priority',
  'startDate',
  'endDate',
  'departmentId',
] as const

const statusOptions = editableProjectStatuses.map((value) => ({
  value,
  label: projectStatusLabels[value],
}))

const priorityOptions = projectPriorities.map((value) => ({
  value,
  label: projectPriorityLabels[value],
}))

interface ProjectFormProps {
  initialValues: ProjectFormValues
  /** Mode ubah: kode tidak bisa diubah dan status bisa dipilih. */
  editing: boolean
  departments: Department[]
  submitLabel: string
  /** Melempar error bila server menolak; error validasi dipasang ke field yang sesuai. */
  onSubmit: (values: ProjectFormValues) => Promise<void>
  onCancel: () => void
}

export function ProjectForm({
  initialValues,
  editing,
  departments,
  submitLabel,
  onSubmit,
  onCancel,
}: ProjectFormProps) {
  const [formError, setFormError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ProjectFormValues>({
    resolver: zodResolver(projectSchema),
    defaultValues: initialValues,
  })

  const submit = async (values: ProjectFormValues) => {
    setFormError(null)
    try {
      await onSubmit(values)
    } catch (e) {
      setFormError(applyServerErrors(e, setError, fields))
    }
  }

  const item = (name: keyof ProjectFormValues, label: string, required = false) => ({
    label,
    htmlFor: `project-${name}`,
    required,
    validateStatus: errors[name] ? ('error' as const) : ('' as const),
    help: errors[name]?.message,
  })

  return (
    <Form layout="vertical" onFinish={handleSubmit(submit)} noValidate>
      {formError && (
        <Alert
          type="error"
          message={formError}
          showIcon
          style={{ marginBottom: 16 }}
          role="alert"
        />
      )}
      <Form.Item {...item('code', 'Kode', true)}>
        <Controller
          name="code"
          control={control}
          render={({ field }) => (
            <Input {...field} id="project-code" disabled={editing} placeholder="mis. WEB-01" />
          )}
        />
      </Form.Item>
      <Form.Item {...item('name', 'Nama', true)}>
        <Controller
          name="name"
          control={control}
          render={({ field }) => <Input {...field} id="project-name" />}
        />
      </Form.Item>
      <Form.Item {...item('description', 'Deskripsi')}>
        <Controller
          name="description"
          control={control}
          render={({ field }) => <Input.TextArea {...field} id="project-description" rows={3} />}
        />
      </Form.Item>
      {editing && (
        <Form.Item {...item('status', 'Status', true)}>
          <Controller
            name="status"
            control={control}
            render={({ field }) => (
              <Select {...field} id="project-status" options={statusOptions} />
            )}
          />
        </Form.Item>
      )}
      <Form.Item {...item('priority', 'Prioritas', true)}>
        <Controller
          name="priority"
          control={control}
          render={({ field }) => (
            <Select {...field} id="project-priority" options={priorityOptions} />
          )}
        />
      </Form.Item>
      <Flex gap={12}>
        <Form.Item {...item('startDate', 'Tanggal mulai')} style={{ flex: 1 }}>
          <Controller
            name="startDate"
            control={control}
            render={({ field }) => <Input {...field} id="project-startDate" type="date" />}
          />
        </Form.Item>
        <Form.Item {...item('endDate', 'Tanggal selesai')} style={{ flex: 1 }}>
          <Controller
            name="endDate"
            control={control}
            render={({ field }) => <Input {...field} id="project-endDate" type="date" />}
          />
        </Form.Item>
      </Flex>
      <Form.Item {...item('departmentId', 'Departemen')}>
        <Controller
          name="departmentId"
          control={control}
          render={({ field }) => (
            <Select
              id="project-departmentId"
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Tanpa departemen"
              value={field.value ?? undefined}
              onChange={(value?: string) => field.onChange(value ?? null)}
              onBlur={field.onBlur}
              options={departments.map((d) => ({ value: d.id, label: d.name }))}
            />
          )}
        />
      </Form.Item>
      <Flex justify="end" gap={8}>
        <Button onClick={onCancel} disabled={isSubmitting}>
          Batal
        </Button>
        <Button type="primary" htmlType="submit" loading={isSubmitting}>
          {submitLabel}
        </Button>
      </Flex>
    </Form>
  )
}
