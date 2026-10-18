import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Button, Form, Input } from 'antd'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { authApi } from '@/features/auth/api/authApi'
import { loginErrorMessage } from '@/features/auth/api/loginErrorMessage'
import { useAuthStore } from '@/features/auth/store/authStore'

const loginSchema = z.object({
  email: z.string().trim().min(1, 'Email wajib diisi').email('Format email tidak valid'),
  password: z.string().min(1, 'Password wajib diisi'),
})

type LoginValues = z.infer<typeof loginSchema>

export function LoginForm({ onSuccess }: { onSuccess: () => void }) {
  const setSession = useAuthStore((s) => s.setSession)
  const [error, setError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  })

  const onSubmit = async (values: LoginValues) => {
    setError(null)
    try {
      const { accessToken } = await authApi.login({
        email: values.email.trim(),
        password: values.password,
      })
      setSession(accessToken)
      onSuccess()
    } catch (e) {
      setError(loginErrorMessage(e))
    }
  }

  return (
    <Form layout="vertical" onFinish={handleSubmit(onSubmit)} noValidate>
      {error && (
        <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} role="alert" />
      )}
      <Form.Item
        label="Email"
        htmlFor="email"
        validateStatus={errors.email ? 'error' : ''}
        help={errors.email?.message}
      >
        <Controller
          name="email"
          control={control}
          render={({ field }) => (
            <Input {...field} id="email" type="email" autoComplete="username" autoFocus />
          )}
        />
      </Form.Item>
      <Form.Item
        label="Password"
        htmlFor="password"
        validateStatus={errors.password ? 'error' : ''}
        help={errors.password?.message}
      >
        <Controller
          name="password"
          control={control}
          render={({ field }) => (
            <Input.Password {...field} id="password" autoComplete="current-password" />
          )}
        />
      </Form.Item>
      <Button type="primary" htmlType="submit" block loading={isSubmitting}>
        Masuk
      </Button>
    </Form>
  )
}
