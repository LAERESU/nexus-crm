import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { App as AntdApp } from 'antd'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '@/features/auth/store/authStore'
import type { Permission, Role } from '@/features/roles/api/rolesApi'
import RoleManagementPage from '@/features/roles/pages/RoleManagementPage'
import { httpClient } from '@/shared/api/httpClient'
import { respond, slow } from '@/test/http'
import { fakeJwt } from '@/test/tokens'

const originalAdapter = httpClient.defaults.adapter

const catalog: Permission[] = [
  { code: 'user.read', module: 'user', description: 'Melihat user' },
  { code: 'user.manage', module: 'user', description: null },
  { code: 'task.read', module: 'task', description: null },
]

const initialRoles = (): Role[] => [
  {
    id: 'r-admin',
    code: 'TENANT_ADMIN',
    name: 'Admin Tenant',
    description: null,
    isSystem: true,
    permissions: ['user.read', 'user.manage', 'task.read'],
    rowVersion: 1,
  },
  {
    id: 'r-staf',
    code: 'STAF',
    name: 'Staf',
    description: null,
    isSystem: false,
    permissions: ['task.read'],
    rowVersion: 4,
  },
]

interface Write {
  url?: string
  data: { permissions: string[] }
  ifMatch: unknown
}

let ownPermissionLoads = 0

/** Backend tiruan yang menyimpan permission role, seperti server sungguhan. */
function backend({ conflict = false } = {}) {
  const roles = initialRoles()
  const writes: Write[] = []
  httpClient.defaults.adapter = async (config) => {
    if (config.method === 'put') {
      const data = JSON.parse(config.data as string) as { permissions: string[] }
      writes.push({ url: config.url, data, ifMatch: config.headers.get('If-Match') })
      const role = roles.find((r) => config.url === `/roles/${r.id}/permissions`)
      if (!role) return respond(config, 404)
      if (conflict) {
        // Admin lain sudah mengubah role ini.
        role.permissions = ['user.read']
        role.rowVersion += 1
        return respond(config, 409, { title: 'Konflik versi data' })
      }
      role.permissions = data.permissions
      role.rowVersion += 1
      return respond(config, 200, { ...role })
    }
    if (config.url === '/permissions') return respond(config, 200, catalog)
    if (config.url === '/users/me/permissions') {
      ownPermissionLoads += 1
      return respond(config, 200, {
        permissions: ['role.read', 'role.manage'].map((code) => ({ code, scopeType: 'global' })),
      })
    }
    return respond(config, 200, {
      items: roles.map((r) => ({ ...r })),
      page: 1,
      size: 100,
      totalCount: roles.length,
    })
  }
  return writes
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([
    { path: '/', element: <RoleManagementPage /> },
    { path: '/lain', element: <p>Halaman lain</p> },
  ])
  const view = render(
    <QueryClientProvider client={queryClient}>
      <AntdApp>
        <RouterProvider router={router} />
      </AntdApp>
    </QueryClientProvider>,
  )
  return { ...view, router }
}

const box = (label: string) => screen.findByRole('checkbox', { name: label }, slow)

function grantPermissions(...codes: string[]) {
  useAuthStore.getState().login(fakeJwt())
  useAuthStore.getState().setPermissions(
    useAuthStore.getState().generation,
    'user-1',
    codes.map((code) => ({ code, scopeType: 'global' })),
  )
}

describe('RoleManagementPage', () => {
  beforeEach(() => grantPermissions('role.read', 'role.manage'))
  afterEach(() => {
    ownPermissionLoads = 0
    httpClient.defaults.adapter = originalAdapter
  })

  it('menampilkan matriks per modul; role sistem read-only', async () => {
    backend()
    renderPage()

    expect(await box('Staf: task.read')).toBeChecked()
    expect(await box('Staf: user.read')).not.toBeChecked()
    expect(await box('Staf: user.read')).toBeEnabled()
    expect(await box('Admin Tenant: user.manage')).toBeChecked()
    expect(await box('Admin Tenant: user.manage')).toBeDisabled()
    expect(screen.getAllByText('Sistem').length).toBeGreaterThan(0)
    // Sel modul digabung: satu sel "user" untuk dua permission.
    expect(screen.getAllByText('user')).toHaveLength(1)
    expect(screen.queryByRole('button', { name: /Simpan/ })).not.toBeInTheDocument()
  })

  it('perubahan checkbox tersimpan dan bertahan setelah halaman dimuat ulang', async () => {
    const writes = backend()
    const first = renderPage()
    const user = userEvent.setup()

    await user.click(await box('Staf: user.read'))
    await user.click(await box('Staf: task.read'))
    await user.click(screen.getByRole('button', { name: 'Simpan Staf' }))

    await waitFor(() => expect(writes).toHaveLength(1), slow)
    expect(writes[0]).toMatchObject({
      url: '/roles/r-staf/permissions',
      ifMatch: '"4"',
      data: { permissions: ['user.read'] },
    })
    await waitFor(
      () => expect(screen.queryByRole('button', { name: 'Simpan Staf' })).not.toBeInTheDocument(),
      slow,
    )

    // Muat ulang: halaman baru dengan cache kosong membaca dari server.
    first.unmount()
    renderPage()

    expect(await box('Staf: user.read')).toBeChecked()
    expect(await box('Staf: task.read')).not.toBeChecked()
  })

  it('mengembalikan checkbox ke keadaan tersimpan menghilangkan tombol simpan', async () => {
    backend()
    renderPage()
    const user = userEvent.setup()

    await user.click(await box('Staf: user.read'))
    expect(screen.getByRole('button', { name: 'Simpan Staf' })).toBeInTheDocument()

    await user.click(await box('Staf: user.read'))
    expect(screen.queryByRole('button', { name: 'Simpan Staf' })).not.toBeInTheDocument()
  })

  it('konflik 409 membatalkan perubahan lokal dan menampilkan keadaan server', async () => {
    backend({ conflict: true })
    renderPage()
    const user = userEvent.setup()

    await user.click(await box('Staf: user.manage'))
    await user.click(screen.getByRole('button', { name: 'Simpan Staf' }))

    expect(await screen.findByText(/sudah diubah pengguna lain/, {}, slow)).toBeInTheDocument()
    await waitFor(async () => expect(await box('Staf: user.read')).toBeChecked(), slow)
    expect(await box('Staf: user.manage')).not.toBeChecked()
    expect(await box('Staf: task.read')).not.toBeChecked()
    expect(screen.queryByRole('button', { name: 'Simpan Staf' })).not.toBeInTheDocument()
  })

  it('pindah halaman dengan perubahan belum disimpan meminta konfirmasi', async () => {
    backend()
    const { router } = renderPage()
    const user = userEvent.setup()

    await user.click(await box('Staf: user.read'))
    void router.navigate('/lain')

    expect(await screen.findByText('Tinggalkan halaman?', {}, slow)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/')

    await user.click(screen.getByRole('button', { name: 'Tinggalkan' }))
    expect(await screen.findByText('Halaman lain', {}, slow)).toBeInTheDocument()
  })

  it('menyimpan role memuat ulang permission user yang login', async () => {
    const writes = backend()
    renderPage()
    const user = userEvent.setup()

    await user.click(await box('Staf: user.read'))
    await user.click(screen.getByRole('button', { name: 'Simpan Staf' }))

    await waitFor(() => expect(writes).toHaveLength(1), slow)
    await waitFor(() => expect(ownPermissionLoads).toBe(1), slow)
  })

  it('tanpa role.manage seluruh matriks read-only', async () => {
    grantPermissions('role.read')
    backend()
    renderPage()

    expect(await box('Staf: task.read')).toBeDisabled()
    expect(await box('Staf: user.read')).toBeDisabled()
  })
})
