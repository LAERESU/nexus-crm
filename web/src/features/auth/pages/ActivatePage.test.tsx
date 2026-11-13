import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { AppProviders } from '@/app/providers'
import { routes } from '@/app/router'
import { useAuthStore } from '@/features/auth/store/authStore'
import { httpClient } from '@/shared/api/httpClient'
import { respond, slow } from '@/test/http'

const originalAdapter = httpClient.defaults.adapter

/** Backend tiruan: tanpa sesi; `activate` menentukan jawaban `POST /auth/activate`. */
function backend(activate: { status: number; data?: unknown } = { status: 204 }) {
  const requests: { url?: string; data?: unknown }[] = []
  httpClient.defaults.adapter = async (config) => {
    if (config.url === '/auth/refresh') return respond(config, 401)
    requests.push({ url: config.url, data: JSON.parse(config.data as string) })
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
      generation: s.generation + 1,
    })),
  )
  afterEach(() => {
    httpClient.defaults.adapter = originalAdapter
  })

  it('terbuka tanpa login, mengirim token dari tautan, lalu mengarahkan ke halaman masuk', async () => {
    const requests = backend()
    const router = renderAt('/activate?token=7.abc%2Bdef')

    const user = await fill('Rahasia#Baru2026')

    expect(await screen.findByText('Akun aktif', {}, slow)).toBeInTheDocument()
    expect(requests).toEqual([
      { url: '/auth/activate', data: { token: '7.abc+def', password: 'Rahasia#Baru2026' } },
    ])

    await user.click(screen.getByRole('button', { name: 'Ke halaman masuk' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })

  it('password pendek atau konfirmasi berbeda ditolak tanpa memanggil server', async () => {
    const requests = backend()
    renderAt('/activate?token=7.abc')

    await fill('pendek', 'lain')

    expect(await screen.findByText('Password minimal 10 karakter')).toBeInTheDocument()
    expect(screen.getByText('Konfirmasi password tidak sama')).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })

  it('tautan kedaluwarsa atau sudah dipakai menampilkan pesan dari server', async () => {
    backend({
      status: 422,
      data: { detail: 'Tautan aktivasi tidak valid atau sudah kedaluwarsa.' },
    })
    renderAt('/activate?token=7.basi')

    await fill('Rahasia#Baru2026')

    expect(await screen.findByRole('alert', {}, slow)).toHaveTextContent(
      'Tautan aktivasi tidak valid atau sudah kedaluwarsa.',
    )
  })

  it('error validasi server tampil di field password', async () => {
    backend({ status: 400, data: { errors: { Password: ['Password terlalu lemah.'] } } })
    renderAt('/activate?token=7.abc')

    await fill('Rahasia#Baru2026')

    expect(await screen.findByText('Password terlalu lemah.', {}, slow)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('tanpa token menampilkan petunjuk, bukan form', async () => {
    backend()
    renderAt('/activate')

    expect(await screen.findByText('Tautan tidak lengkap', {}, slow)).toBeInTheDocument()
    expect(screen.queryByLabelText('Password baru')).not.toBeInTheDocument()
  })
})
