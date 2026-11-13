import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Button, Form, Input, Result, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { authApi } from '@/features/auth/api/authApi'
import { useAuthStore } from '@/features/auth/store/authStore'
import { parseProblem, problemMessage } from '@/shared/api/problemDetails'

// Sama dengan backend (ActivateAccountCommandValidator).
const MIN_PASSWORD_LENGTH = 10
const MAX_PASSWORD_LENGTH = 200

const activateSchema = z
  .object({
    password: z
      .string()
      .min(MIN_PASSWORD_LENGTH, `Password minimal ${MIN_PASSWORD_LENGTH} karakter`)
      .max(MAX_PASSWORD_LENGTH, `Password maksimal ${MAX_PASSWORD_LENGTH} karakter`),
    confirm: z.string(),
  })
  .refine((v) => v.password === v.confirm, {
    path: ['confirm'],
    message: 'Konfirmasi password tidak sama',
  })

type ActivateValues = z.infer<typeof activateSchema>

/** Token dari tautan undangan: di fragment (`#token=`, tidak ikut terkirim ke server) atau di query string. */
function readToken(hash: string, search: string): string {
  const fromHash = new URLSearchParams(hash.replace(/^#/, '')).get('token')
  return (fromHash ?? new URLSearchParams(search).get('token') ?? '').trim()
}

type Outcome = 'form' | 'done' | 'invalid-link'

/** Halaman tujuan tautan di email undangan (`/activate#token=...`): user mengisi password pertamanya. */
export default function ActivatePage() {
  const location = useLocation()
  const navigate = useNavigate()
  // Token dibaca sekali lalu dibuang dari address bar dan riwayat browser.
  const [token] = useState(() => readToken(location.hash, location.search))
  useEffect(() => {
    if (location.hash || location.search) navigate('/activate', { replace: true })
  }, [location.hash, location.search, navigate])

  const [outcome, setOutcome] = useState<Outcome>('form')
  const [error, setError] = useState<string | null>(null)
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

  if (outcome === 'done')
    return (
      <Result
        status="success"
        title="Akun aktif"
        subTitle="Password tersimpan. Silakan masuk dengan email yang menerima undangan dan password Anda."
        extra={toLogin}
      />
    )

  // Tautan salah, kedaluwarsa, atau sudah dipakai; server sengaja tidak membedakannya.
  if (outcome === 'invalid-link')
    return (
      <Result
        status="warning"
        title="Tautan tidak berlaku"
        subTitle="Tautan aktivasi ini tidak valid, sudah kedaluwarsa, atau sudah dipakai. Kalau akun Anda sudah aktif, silakan masuk; kalau belum, minta admin mengirim undangan baru."
        extra={toLogin}
      />
    )

  /** Sesi orang lain di browser ini diakhiri, agar user baru masuk sebagai dirinya sendiri. */
  const endOtherSession = async () => {
    if (useAuthStore.getState().status !== 'authenticated') return
    try {
      await authApi.logout()
    } catch {
      // Sesi lokal tetap diakhiri walau server tidak terjangkau.
    }
    useAuthStore.getState().clear()
  }

  const onSubmit = async (values: ActivateValues) => {
    setError(null)
    try {
      await authApi.activate(token, values.password)
    } catch (e) {
      const problem = parseProblem(e)
      const passwordErrors = problem.fieldErrors.password
      if (problem.status === 422 || problem.fieldErrors.token) setOutcome('invalid-link')
      else if (passwordErrors)
        setFieldError('password', { type: 'server', message: passwordErrors.join(' ') })
      else setError(problemMessage(problem))
      return
    }
    await endOtherSession()
    setOutcome('done')
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
          help={errors.password?.message ?? `Minimal ${MIN_PASSWORD_LENGTH} karakter.`}
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
