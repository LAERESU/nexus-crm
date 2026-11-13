import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Button, Flex, Form, Input, Select } from 'antd'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import type { Department } from '@/features/users/api/usersApi'
import { applyServerErrors } from '@/shared/api/problemDetails'

// Batas panjang sama dengan backend (User.EmailMaxLength dst.).
const userSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, 'Email wajib diisi')
    .max(320, 'Email maksimal 320 karakter')
    .email('Format email tidak valid'),
  fullName: z
    .string()
    .trim()
    .min(1, 'Nama lengkap wajib diisi')
    .max(200, 'Nama lengkap maksimal 200 karakter'),
  jobTitle: z.string().trim().max(200, 'Jabatan maksimal 200 karakter'),
  departmentId: z.string().nullable(),
})

export type UserFormValues = z.infer<typeof userSchema>

const fields = ['email', 'fullName', 'jobTitle', 'departmentId'] as const

interface UserFormProps {
  initialValues: UserFormValues
  /** Email tidak bisa diubah setelah user dibuat. */
  emailReadOnly?: boolean
  departments: Department[]
  submitLabel: string
  /** Melempar error bila server menolak; error validasi dipasang ke field yang sesuai. */
  onSubmit: (values: UserFormValues) => Promise<void>
  onCancel: () => void
}

export function UserForm({
  initialValues,
  emailReadOnly = false,
  departments,
  submitLabel,
  onSubmit,
  onCancel,
}: UserFormProps) {
  const [formError, setFormError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<UserFormValues>({ resolver: zodResolver(userSchema), defaultValues: initialValues })

  const submit = async (values: UserFormValues) => {
    setFormError(null)
    try {
      await onSubmit(values)
    } catch (e) {
      setFormError(applyServerErrors(e, setError, fields))
    }
  }

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
      <Form.Item
        label="Email"
        htmlFor="user-email"
        required
        validateStatus={errors.email ? 'error' : ''}
        help={errors.email?.message}
      >
        <Controller
          name="email"
          control={control}
          render={({ field }) => (
            <Input {...field} id="user-email" type="email" disabled={emailReadOnly} />
          )}
        />
      </Form.Item>
      <Form.Item
        label="Nama lengkap"
        htmlFor="user-fullName"
        required
        validateStatus={errors.fullName ? 'error' : ''}
        help={errors.fullName?.message}
      >
        <Controller
          name="fullName"
          control={control}
          render={({ field }) => <Input {...field} id="user-fullName" />}
        />
      </Form.Item>
      <Form.Item
        label="Jabatan"
        htmlFor="user-jobTitle"
        validateStatus={errors.jobTitle ? 'error' : ''}
        help={errors.jobTitle?.message}
      >
        <Controller
          name="jobTitle"
          control={control}
          render={({ field }) => <Input {...field} id="user-jobTitle" />}
        />
      </Form.Item>
      <Form.Item
        label="Departemen"
        htmlFor="user-departmentId"
        validateStatus={errors.departmentId ? 'error' : ''}
        help={errors.departmentId?.message}
      >
        <Controller
          name="departmentId"
          control={control}
          render={({ field }) => (
            <Select
              id="user-departmentId"
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
