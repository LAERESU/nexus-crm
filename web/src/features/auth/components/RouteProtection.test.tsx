import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AxiosError } from 'axios'
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { AppProviders } from '@/app/providers'
import { routes } from '@/app/router'
import { Can } from '@/features/auth/components/Can'
import { useAuthStore } from '@/features/auth/store/authStore'
import type { PermissionGrant } from '@/features/auth/store/authStore'
import { httpClient } from '@/shared/api/httpClient'
import { fakeJwt } from '@/test/tokens'

const originalAdapter = httpClient.defaults.adapter
const slow = { timeout: 5000 }

const grant = (code: string, scopeType = 'global'): PermissionGrant => ({ code, scopeType })

function respond(config: InternalAxiosRequestConfig, status: number, data: unknown = {}) {
  const response: AxiosResponse = { data, status, statusText: String(status), headers: {}, config }
  return status >= 400
    ? Promise.reject(new AxiosError('gagal', String(status), config, undefined, response))
    : Promise.resolve(response)
}

/** Backend tiruan: sesi dipulihkan dari cookie; `permissions` menjawab `GET /users/{id}/permissions`. */
function backend(permissions: () => PermissionGrant[] | 'gagal') {
  const urls: string[] = []
  httpClient.defaults.adapter = async (config) => {
    urls.push(config.url ?? '')
    if (config.url === '/auth/refresh')
      return respond(config, 200, { accessToken: fakeJwt({ sub: 'u-1' }) })
    if (config.url === '/users/u-1/permissions') {
      const result = permissions()
      return result === 'gagal'
        ? respond(config, 503)
        : respond(config, 200, { userId: 'u-1', roles: [], permissions: result })
    }
    return respond(config, 200, { items: [], page: 1, size: 20, totalCount: 0 })
  }
  return urls
}

function renderApp(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>,
  )
  return router
}

describe('proteksi route dan komponen Can', () => {
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

  it('user tanpa izin yang membuka URL admin diarahkan ke /403 dan tidak melihat menu Admin', async () => {
    backend(() => [grant('task.read')])
    const router = renderApp('/admin/users')

    expect(
      await screen.findByText('Anda tidak memiliki akses ke halaman ini.', {}, slow),
    ).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/403')
    expect(screen.queryByText('Admin')).not.toBeInTheDocument()
  })

  it('user dengan user.read membuka halaman Pengguna dan melihat menu Admin', async () => {
    const urls = backend(() => [grant('user.read')])
    const router = renderApp('/admin/users')

    expect(await screen.findByRole('heading', { name: 'Pengguna' }, slow)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/admin/users')
    expect(screen.getByText('Admin')).toBeInTheDocument()
    // Tanpa user.manage, tombol aksi tersembunyi.
    expect(screen.queryByRole('button', { name: /Undang user/ })).not.toBeInTheDocument()
    expect(urls.filter((u) => u === '/users/u-1/permissions')).toHaveLength(1)
  })

  it('gagal memuat permission menampilkan pesan dengan tombol coba lagi, bukan /403', async () => {
    let fail = true
    backend(() => (fail ? 'gagal' : [grant('user.read')]))
    const router = renderApp('/admin/users')
    const user = userEvent.setup()

    expect(await screen.findByText('Hak akses gagal dimuat', {}, slow)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/admin/users')

    fail = false
    await user.click(screen.getByRole('button', { name: 'Coba lagi' }))

    expect(await screen.findByRole('heading', { name: 'Pengguna' }, slow)).toBeInTheDocument()
  })

  it('permission sesi sebelumnya tidak terbawa ke login berikutnya', async () => {
    backend(() => [grant('user.read')])
    renderApp('/')
    await waitFor(() => expect(useAuthStore.getState().permissionsStatus).toBe('ready'), slow)

    useAuthStore.getState().login(fakeJwt({ sub: 'u-2' }))

    expect(useAuthStore.getState().permissions).toEqual([])
    expect(useAuthStore.getState().hasPermission('user.read')).toBe(false)
  })

  it('Can menyembunyikan aksi; permission kelola hanya berlaku di scope global', () => {
    useAuthStore.getState().login(fakeJwt())
    const generation = useAuthStore.getState().generation
    useAuthStore
      .getState()
      .setPermissions(generation, [
        grant('user.manage', 'department'),
        grant('task.read', 'project'),
      ])

    render(
      <>
        <Can permission="user.manage">
          <button>Kelola user</button>
        </Can>
        <Can permission="task.read" fallback={<span>tidak boleh</span>}>
          <button>Lihat tugas</button>
        </Can>
        <Can permission="sop.approve" fallback={<span>tanpa izin setuju</span>}>
          <button>Setujui</button>
        </Can>
      </>,
    )

    expect(screen.queryByRole('button', { name: 'Kelola user' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Lihat tugas' })).toBeInTheDocument()
    expect(screen.getByText('tanpa izin setuju')).toBeInTheDocument()
  })
})
