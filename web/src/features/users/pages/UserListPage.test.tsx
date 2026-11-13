import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { App as AntdApp } from 'antd'
import { AxiosError } from 'axios'
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '@/features/auth/store/authStore'
import type { User } from '@/features/users/api/usersApi'
import UserListPage from '@/features/users/pages/UserListPage'
import { httpClient } from '@/shared/api/httpClient'
import { fakeJwt } from '@/test/tokens'

const originalAdapter = httpClient.defaults.adapter
const slow = { timeout: 5000 }

const budi: User = {
  id: 'u-1',
  email: 'budi@demo.local',
  fullName: 'Budi Santoso',
  jobTitle: 'Analis',
  status: 'active',
  departmentId: 'd-1',
  departmentName: 'Keuangan',
  rowVersion: 3,
  createdAt: '2026-11-01T00:00:00Z',
}

interface Recorded {
  method?: string
  url?: string
  params?: Record<string, unknown>
  data?: unknown
  ifMatch?: unknown
}

function respond(config: InternalAxiosRequestConfig, status: number, data: unknown = {}) {
  const response: AxiosResponse = { data, status, statusText: String(status), headers: {}, config }
  return status >= 400
    ? Promise.reject(new AxiosError('gagal', String(status), config, undefined, response))
    : Promise.resolve(response)
}

/** Backend tiruan; `write` menentukan jawaban untuk POST/PUT/PATCH. */
function backend(write: { status: number; data?: unknown } = { status: 200, data: budi }) {
  const requests: Recorded[] = []
  httpClient.defaults.adapter = async (config) => {
    requests.push({
      method: config.method,
      url: config.url,
      params: config.params as Record<string, unknown> | undefined,
      data: typeof config.data === 'string' ? JSON.parse(config.data) : config.data,
      ifMatch: config.headers.get('If-Match'),
    })
    if (config.method !== 'get') return respond(config, write.status, write.data)
    if (config.url === '/departments')
      return respond(config, 200, {
        items: [{ id: 'd-1', code: 'KEU', name: 'Keuangan', parentId: null }],
        page: 1,
        size: 100,
        totalCount: 1,
      })
    return respond(config, 200, { items: [budi], page: 1, size: 20, totalCount: 45 })
  }
  return requests
}

const listRequests = (requests: Recorded[]) =>
  requests.filter((r) => r.method === 'get' && r.url === '/users')

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <AntdApp>
        <UserListPage />
      </AntdApp>
    </QueryClientProvider>,
  )
}

describe('UserListPage', () => {
  beforeEach(() => useAuthStore.getState().login(fakeJwt()))
  afterEach(() => {
    httpClient.defaults.adapter = originalAdapter
  })

  it('menampilkan user dari server beserta total untuk paging', async () => {
    const requests = backend()
    renderPage()

    expect(await screen.findByText('Budi Santoso', {}, slow)).toBeInTheDocument()
    expect(screen.getByText('45 user')).toBeInTheDocument()
    expect(listRequests(requests)[0]?.params).toMatchObject({ page: 1, size: 20 })
  })

  it('pencarian di-debounce: satu request untuk seluruh ketikan, kembali ke halaman 1', async () => {
    const requests = backend()
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('Budi Santoso', {}, slow)

    await user.click(screen.getByTitle('2'))
    await waitFor(() => expect(listRequests(requests).at(-1)?.params?.page).toBe(2))

    await user.type(screen.getByLabelText('Cari user'), 'budi')
    await waitFor(() => expect(listRequests(requests).at(-1)?.params?.search).toBe('budi'), slow)

    const searches = listRequests(requests).filter((r) => r.params?.search !== undefined)
    expect(searches).toHaveLength(1)
    expect(searches[0]?.params).toMatchObject({ search: 'budi', page: 1 })
  })

  it('error validasi dari server tampil di field yang sesuai', async () => {
    const requests = backend({
      status: 400,
      data: { title: 'Validasi gagal', errors: { Email: ['Email tidak valid menurut server.'] } },
    })
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Undang user/ }, slow))
    const dialog = await screen.findByRole('dialog', {}, slow)
    await user.type(within(dialog).getByLabelText(/Email/), 'baru@demo.local')
    await user.type(within(dialog).getByLabelText(/Nama lengkap/), 'User Baru')
    await user.click(within(dialog).getByRole('button', { name: 'Kirim undangan' }))

    const help = await within(dialog).findByText('Email tidak valid menurut server.', {}, slow)
    expect(help.closest('.ant-form-item')).toContainElement(within(dialog).getByLabelText(/Email/))
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
    expect(requests.find((r) => r.method === 'post')?.data).toEqual({
      email: 'baru@demo.local',
      fullName: 'User Baru',
      jobTitle: null,
      departmentId: null,
    })
  })

  it('aturan bisnis yang dilanggar (422) tampil sebagai pesan form', async () => {
    backend({ status: 422, data: { detail: 'Email sudah dipakai user lain di tenant ini.' } })
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Undang user/ }, slow))
    const dialog = await screen.findByRole('dialog', {}, slow)
    await user.type(within(dialog).getByLabelText(/Email/), 'budi@demo.local')
    await user.type(within(dialog).getByLabelText(/Nama lengkap/), 'Budi')
    await user.click(within(dialog).getByRole('button', { name: 'Kirim undangan' }))

    expect(await within(dialog).findByRole('alert', {}, slow)).toHaveTextContent(
      'Email sudah dipakai user lain di tenant ini.',
    )
  })

  it('edit user mengirim PUT dengan If-Match berisi rowVersion', async () => {
    const requests = backend()
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit' }, slow))
    const name = await screen.findByLabelText(/Nama lengkap/, {}, slow)
    expect(screen.getByLabelText(/Email/)).toBeDisabled()
    await user.clear(name)
    await user.type(name, 'Budi S.')
    await user.click(screen.getByRole('button', { name: 'Simpan' }))

    await waitFor(() => expect(requests.some((r) => r.method === 'put')).toBe(true), slow)
    const put = requests.find((r) => r.method === 'put')
    expect(put).toMatchObject({
      url: '/users/u-1',
      ifMatch: '"3"',
      data: { fullName: 'Budi S.', jobTitle: 'Analis', departmentId: 'd-1' },
    })
  })

  it('menonaktifkan user meminta konfirmasi lalu mengirim PATCH status', async () => {
    const requests = backend()
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Nonaktifkan' }, slow))
    await user.click(await screen.findByRole('button', { name: 'Ya' }, slow))

    await waitFor(() => expect(requests.some((r) => r.method === 'patch')).toBe(true), slow)
    expect(requests.find((r) => r.method === 'patch')).toMatchObject({
      url: '/users/u-1/status',
      ifMatch: '"3"',
      data: { status: 'inactive' },
    })
  })
})
