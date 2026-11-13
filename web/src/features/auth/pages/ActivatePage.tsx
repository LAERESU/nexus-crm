import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Button, Form, Input, Result, Typography } from 'antd'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Link, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { authApi } from '@/features/auth/api/authApi'
import { applyServerErrors } from '@/shared/api/problemDetails'

// Batas panjang sama dengan backend (ActivateAccountCommandValidator).
const activateSchema = z
  .object({
    password: z
      .string()
      .min(10, 'Password minimal 10 karakter')
      .max(200, 'Password maksimal 200 karakter'),
    confirm: z.string(),
  })
  .refine((v) => v.password === v.confirm, {
    path: ['confirm'],
    message: 'Konfirmasi password tidak sama',
  })

type ActivateValues = z.infer<typeof activateSchema>

const fields = ['password'] as const

/** Halaman tujuan tautan di email undangan (`/activate?token=...`): user mengisi password pertamanya. */
export default function ActivatePage() {
  const [params] = useSearchParams()
  const token = params.get('token')?.trim() ?? ''
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const {
    control,
    handleSubmit,
    setError: setFieldError,
    formState: { errors, isSubmitting },
  } = useForm<ActivateValues>({
    resolver: zodResolver(activateSchema),
    defaultValues: { password: '', confirm: '' },
  })

  const toLogin = (
    <Link to="/login">
      <Button type="primary">Ke halaman masuk</Button>
    </Link>
  )

  if (!token)
    return (
      <Result
        status="warning"
        title="Tautan tidak lengkap"
        subTitle="Buka tautan aktivasi langsung dari email undangan Anda."
        extra={toLogin}
      />
    )

  if (done)
    return (
      <Result
        status="success"
        title="Akun aktif"
        subTitle="Password tersimpan. Silakan masuk dengan email dan password Anda."
        extra={toLogin}
      />
    )

  const onSubmit = async (values: ActivateValues) => {
    setError(null)
    try {
      await authApi.activate(token, values.password)
      setDone(true)
    } catch (e) {
      // 422: tautan salah, kedaluwarsa, atau sudah dipakai (pesan dari server sama untuk ketiganya).
      setError(applyServerErrors(e, setFieldError, fields))
    }
  }

  return (
    <>
      <Typography.Title level={3} style={{ textAlign: 'center' }}>
        Aktifkan akun
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ textAlign: 'center' }}>
        Buat password untuk akun NexUS Anda.
      </Typography.Paragraph>
      <Form layout="vertical" onFinish={handleSubmit(onSubmit)} noValidate>
        {error && (
          <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} role="alert" />
        )}
        <Form.Item
          label="Password baru"
          htmlFor="activate-password"
          validateStatus={errors.password ? 'error' : ''}
          help={errors.password?.message ?? 'Minimal 10 karakter.'}
        >
          <Controller
            name="password"
            control={control}
            render={({ field }) => (
              <Input.Password
                {...field}
                id="activate-password"
                autoComplete="new-password"
                autoFocus
              />
            )}
          />
        </Form.Item>
        <Form.Item
          label="Ulangi password"
          htmlFor="activate-confirm"
          validateStatus={errors.confirm ? 'error' : ''}
          help={errors.confirm?.message}
        >
          <Controller
            name="confirm"
            control={control}
            render={({ field }) => (
              <Input.Password {...field} id="activate-confirm" autoComplete="new-password" />
            )}
          />
        </Form.Item>
        <Button type="primary" htmlType="submit" block loading={isSubmitting}>
          Aktifkan akun
        </Button>
      </Form>
    </>
  )
}
