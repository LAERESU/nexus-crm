import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { AppProviders } from '@/app/providers'
import { routes } from '@/app/router'
import { useAuthStore } from '@/features/auth/store/authStore'
import { httpClient } from '@/shared/api/httpClient'
import { respond, slow } from '@/test/http'
import { fakeJwt } from '@/test/tokens'

const originalAdapter = httpClient.defaults.adapter

interface Options {
  /** Jawaban `POST /auth/activate`. */
  activate?: { status: number; data?: unknown }
  /** Ada sesi user lain di browser (cookie refresh masih berlaku). */
  session?: boolean
}

function backend({ activate = { status: 204 }, session = false }: Options = {}) {
  const requests: { url?: string; data?: unknown }[] = []
  httpClient.defaults.adapter = async (config) => {
    if (config.url === '/auth/refresh')
      return session ? respond(config, 200, { accessToken: fakeJwt() }) : respond(config, 401)
    if (config.url === '/users/me/permissions') return respond(config, 200, { permissions: [] })
    requests.push({
      url: config.url,
      data: typeof config.data === 'string' ? JSON.parse(config.data) : undefined,
    })
    if (config.url === '/auth/logout') return respond(config, 204)
    return respond(config, activate.status, activate.data)
  }
  return requests
}

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>,
  )
  return router
}

async function fill(password: string, confirm = password) {
  const user = userEvent.setup()
  await user.type(await screen.findByLabelText('Password baru', {}, slow), password)
  await user.type(screen.getByLabelText('Ulangi password'), confirm)
  await user.click(screen.getByRole('button', { name: 'Aktifkan akun' }))
  return user
}

describe('ActivatePage', () => {
  beforeEach(() =>
    useAuthStore.setState((s) => ({
      accessToken: null,
      user: null,
      status: 'unknown',
      permissions: [],
      permissionsStatus: 'loading',
      generation: s.generation + 1,
    })),
  )
  afterEach(() => {
    httpClient.defaults.adapter = originalAdapter
  })

  it('terbuka tanpa login, mengirim token dari fragment, lalu mengarahkan ke halaman masuk', async () => {
    const requests = backend()
    const router = renderAt('/activate#token=7.abc%2Bdef')

    const user = await fill('Rahasia#Baru2026')

    expect(await screen.findByText('Akun aktif', {}, slow)).toBeInTheDocument()
    expect(requests).toEqual([
      { url: '/auth/activate', data: { token: '7.abc+def', password: 'Rahasia#Baru2026' } },
    ])

    await user.click(screen.getByRole('button', { name: 'Ke halaman masuk' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })

  it('token dibuang dari address bar, dan tautan lama berbentuk query string tetap diterima', async () => {
    const requests = backend()
    const router = renderAt('/activate?token=7.lama')

    await screen.findByLabelText('Password baru', {}, slow)
    await waitFor(() => expect(router.state.location.search).toBe(''))
    expect(router.state.location.hash).toBe('')

    await fill('Rahasia#Baru2026')
    await screen.findByText('Akun aktif', {}, slow)
    expect(requests[0]?.data).toMatchObject({ token: '7.lama' })
  })

  it('password pendek atau konfirmasi berbeda ditolak tanpa memanggil server', async () => {
    const requests = backend()
    renderAt('/activate#token=7.abc')

    await fill('pendek', 'lain')

    expect(await screen.findByText('Password minimal 10 karakter')).toBeInTheDocument()
    expect(screen.getByText('Konfirmasi password tidak sama')).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })

  it('tautan kedaluwarsa atau sudah dipakai (422) memberi jalan ke halaman masuk', async () => {
    backend({
      activate: {
        status: 422,
        // Pesan backend: ActivateAccountCommandHandler.FailureMessage.
        data: { detail: 'Token aktivasi tidak valid atau sudah kedaluwarsa.' },
      },
    })
    renderAt('/activate#token=7.basi')

    await fill('Rahasia#Baru2026')

    expect(await screen.findByText('Tautan tidak berlaku', {}, slow)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ke halaman masuk' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Password baru')).not.toBeInTheDocument()
  })

  it('token yang ditolak validasi server diperlakukan sebagai tautan tidak berlaku', async () => {
    backend({
      activate: {
        status: 400,
        data: { errors: { Token: ["The length of 'Token' must be 128 characters or fewer."] } },
      },
    })
    renderAt('/activate#token=7.rusak')

    await fill('Rahasia#Baru2026')

    expect(await screen.findByText('Tautan tidak berlaku', {}, slow)).toBeInTheDocument()
    expect(screen.queryByText(/must be 128/)).not.toBeInTheDocument()
  })

  it('error validasi server tampil di field password', async () => {
    backend({
      activate: { status: 400, data: { errors: { Password: ['Password terlalu lemah.'] } } },
    })
    renderAt('/activate#token=7.abc')

    await fill('Rahasia#Baru2026')

    expect(await screen.findByText('Password terlalu lemah.', {}, slow)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('server tidak terjangkau: form tetap ada untuk dicoba lagi', async () => {
    backend({ activate: { status: 503 } })
    renderAt('/activate#token=7.abc')

    await fill('Rahasia#Baru2026')

    expect(await screen.findByRole('alert', {}, slow)).toHaveTextContent('Terjadi kesalahan')
    expect(screen.getByLabelText('Password baru')).toBeInTheDocument()
  })

  it('sesi user lain di browser diakhiri setelah aktivasi, sehingga user baru melihat form masuk', async () => {
    const requests = backend({ session: true })
    const router = renderAt('/activate#token=7.abc')
    await waitFor(() => expect(useAuthStore.getState().status).toBe('authenticated'), slow)

    const user = await fill('Rahasia#Baru2026')
    await screen.findByText('Akun aktif', {}, slow)

    expect(requests.map((r) => r.url)).toEqual(['/auth/activate', '/auth/logout'])
    expect(useAuthStore.getState().status).toBe('anonymous')

    await user.click(screen.getByRole('button', { name: 'Ke halaman masuk' }))
    expect(await screen.findByRole('heading', { name: 'Masuk ke NexUS' }, slow)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
  })

  it('tanpa token menampilkan petunjuk, bukan form', async () => {
    backend()
    renderAt('/activate')

    expect(await screen.findByText('Tautan tidak lengkap', {}, slow)).toBeInTheDocument()
    expect(screen.queryByLabelText('Password baru')).not.toBeInTheDocument()
  })
})
