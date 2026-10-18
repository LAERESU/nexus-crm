import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { routes } from '@/app/router'
import { AuthBootstrap } from '@/features/auth/components/AuthBootstrap'
import { useAuthStore } from '@/features/auth/store/authStore'
import { httpClient } from '@/shared/api/httpClient'
import { queryClient } from '@/shared/api/queryClient'
import { fakeJwt } from '@/test/tokens'

const originalAdapter = httpClient.defaults.adapter
// Halaman dimuat lazy; import pertama Ant Design di jsdom bisa lebih dari 1 detik.
const slow = { timeout: 5000 }

function respond(
  config: InternalAxiosRequestConfig,
  status: number,
  data: unknown = {},
  headers: Record<string, string> = {},
) {
  const response: AxiosResponse = { data, status, statusText: String(status), headers, config }
  return status >= 400
    ? Promise.reject(new AxiosError('gagal', String(status), config, undefined, response))
    : Promise.resolve(response)
}

/** Backend tiruan: refresh (pemulihan sesi) dan login dapat diatur hasilnya. */
function backend({ session = false, login = 200 as 200 | 401 | 429 } = {}) {
  const requests: { url?: string; data?: unknown }[] = []
  httpClient.defaults.adapter = async (config) => {
    requests.push({ url: config.url, data: config.data })
    if (config.url === '/auth/refresh')
      return session ? respond(config, 200, { accessToken: fakeJwt() }) : respond(config, 401)
    if (config.url === '/auth/login') {
      if (login === 429) return respond(config, 429, {}, { 'retry-after': '600' })
      return login === 200
        ? respond(config, 200, { accessToken: fakeJwt({ tid: '7' }) })
        : respond(config, 401)
    }
    if (config.url === '/auth/logout') return respond(config, 204)
    const authorized = AxiosHeaders.from(config.headers).get('Authorization')
    return authorized ? respond(config, 200) : respond(config, 401)
  }
  return requests
}

function renderApp(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(
    <AuthBootstrap>
      <RouterProvider router={router} />
    </AuthBootstrap>,
  )
  return router
}

describe('alur login', () => {
  beforeEach(() =>
    useAuthStore.setState((s) => ({
      accessToken: null,
      user: null,
      status: 'unknown',
      // Generasi selalu naik (seperti di aplikasi) agar refresh yang tertinggal dari tes sebelumnya diabaikan.
      generation: s.generation + 1,
    })),
  )
  afterEach(() => {
    httpClient.defaults.adapter = originalAdapter
    vi.restoreAllMocks()
  })

  it('route terproteksi tanpa login diarahkan ke /login', async () => {
    backend({ session: false })
    const router = renderApp('/')

    expect(await screen.findByRole('heading', { name: 'Masuk ke NexUS' }, slow)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
  })

  it('login berhasil membawa user ke Dashboard tanpa menyimpan token di storage', async () => {
    const requests = backend({ session: false })
    const router = renderApp('/')
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('Email', {}, slow), 'admin@demo.local')
    await user.type(screen.getByLabelText('Password'), 'Rahasia#2026')
    await user.click(screen.getByRole('button', { name: 'Masuk' }))

    expect(await screen.findByRole('heading', { name: 'Dashboard' }, slow)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/')
    expect(requests.find((r) => r.url === '/auth/login')?.data).toContain('admin@demo.local')
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })

  it('refresh halaman memulihkan sesi dari cookie sehingga user tidak keluar', async () => {
    backend({ session: true })
    renderApp('/')

    expect(await screen.findByRole('heading', { name: 'Dashboard' }, slow)).toBeInTheDocument()
    expect(useAuthStore.getState().status).toBe('authenticated')
  })

  it('login salah menampilkan pesan generik', async () => {
    backend({ session: false, login: 401 })
    renderApp('/login')
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('Email', {}, slow), 'admin@demo.local')
    await user.type(screen.getByLabelText('Password'), 'salah')
    await user.click(screen.getByRole('button', { name: 'Masuk' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Email atau password salah.')
  })

  it('terlalu banyak percobaan menampilkan waktu tunggu dari Retry-After', async () => {
    backend({ session: false, login: 429 })
    renderApp('/login')
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('Email', {}, slow), 'admin@demo.local')
    await user.type(screen.getByLabelText('Password'), 'x')
    await user.click(screen.getByRole('button', { name: 'Masuk' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Coba lagi dalam 10 menit')
  })

  it('validasi form menolak email tidak valid tanpa memanggil server', async () => {
    const requests = backend({ session: false })
    renderApp('/login')
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('Email', {}, slow), 'bukan-email')
    await user.type(screen.getByLabelText('Password'), 'x')
    await user.click(screen.getByRole('button', { name: 'Masuk' }))

    expect(await screen.findByText('Format email tidak valid')).toBeInTheDocument()
    expect(requests.some((r) => r.url === '/auth/login')).toBe(false)
  })

  it('tombol Keluar memanggil logout dan kembali ke /login', async () => {
    const requests = backend({ session: true })
    const router = renderApp('/')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Keluar/ }, slow))

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(requests.some((r) => r.url === '/auth/logout')).toBe(true)
    expect(useAuthStore.getState().status).toBe('anonymous')
  })

  it('logout membersihkan cache data server agar user berikutnya tidak melihat data sebelumnya', async () => {
    backend({ session: true })
    renderApp('/')
    queryClient.setQueryData(['projects', 'list'], [{ id: 'p-tenant-7' }])
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Keluar/ }, slow))

    await waitFor(() => expect(queryClient.getQueryData(['projects', 'list'])).toBeUndefined())
  })

  it('setelah login kembali ke halaman asal lengkap dengan query string', async () => {
    backend({ session: false })
    const router = renderApp('/403?tab=2#bagian')
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('Email', {}, slow), 'admin@demo.local')
    await user.type(screen.getByLabelText('Password'), 'Rahasia#2026')
    await user.click(screen.getByRole('button', { name: 'Masuk' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/403'))
    expect(router.state.location.search).toBe('?tab=2')
    expect(router.state.location.hash).toBe('#bagian')
  })
})
