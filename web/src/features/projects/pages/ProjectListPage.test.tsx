import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { App as AntdApp } from 'antd'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '@/features/auth/store/authStore'
import type { Project } from '@/features/projects/api/projectsApi'
import ProjectListPage from '@/features/projects/pages/ProjectListPage'
import { httpClient } from '@/shared/api/httpClient'
import { respond, slow } from '@/test/http'
import { fakeJwt } from '@/test/tokens'

const originalAdapter = httpClient.defaults.adapter

const portal = (): Project => ({
  id: 'p-1',
  code: 'WEB',
  name: 'Portal Karyawan',
  description: null,
  status: 'active',
  priority: 'medium',
  startDate: '2026-12-01',
  endDate: '2026-12-31',
  progressPct: 0,
  ownerId: 'user-1',
  departmentId: 'd-1',
  rowVersion: 3,
  createdAt: '2026-11-01T00:00:00Z',
})

interface Recorded {
  method?: string
  url?: string
  params?: Record<string, unknown>
  data?: unknown
  ifMatch?: unknown
}

/** Perubahan oleh orang lain langsung di backend tiruan (tanpa lewat halaman). */
let changeBehind: (change: Partial<Project>) => void = () => {}

/** Backend tiruan yang menyimpan daftar proyek, seperti server sungguhan (If-Match diperiksa). */
function backend({
  conflictOnUpdate = false,
  createStatus = 201,
  createBody = {} as unknown,
} = {}) {
  let projects = [portal()]
  changeBehind = (change) => {
    projects = projects.map((p) =>
      p.id === 'p-1' ? { ...p, ...change, rowVersion: p.rowVersion + 1 } : p,
    )
  }
  const requests: Recorded[] = []
  httpClient.defaults.adapter = async (config) => {
    const data = typeof config.data === 'string' ? JSON.parse(config.data) : undefined
    requests.push({
      method: config.method,
      url: config.url,
      params: config.params as Record<string, unknown> | undefined,
      data,
      ifMatch: config.headers.get('If-Match'),
    })
    if (config.url === '/departments')
      return respond(config, 200, {
        items: [{ id: 'd-1', code: 'KEU', name: 'Keuangan', parentId: null }],
        page: 1,
        size: 100,
        totalCount: 1,
      })
    if (config.method === 'post') {
      if (createStatus !== 201) return respond(config, createStatus, createBody)
      const created: Project = {
        ...portal(),
        ...data,
        id: 'p-2',
        code: data.code.toUpperCase(),
        status: 'planning',
        rowVersion: 0,
      }
      projects = [...projects, created]
      return respond(config, 201, created)
    }
    if (config.method === 'put') {
      if (conflictOnUpdate) changeBehind({ name: 'Diubah Orang Lain' }) // orang lain mengubah tepat sebelum simpan
      const current = projects.find((p) => p.id === 'p-1')!
      if (config.headers.get('If-Match') !== `"${current.rowVersion}"`)
        return respond(config, 409, { title: 'Konflik versi data' })
      projects = projects.map((p) =>
        p.id === 'p-1' ? { ...p, ...data, rowVersion: p.rowVersion + 1 } : p,
      )
      return respond(config, 200, projects[0])
    }
    if (config.method === 'delete') {
      projects = projects.map((p) => (p.id === 'p-1' ? { ...p, status: 'archived' } : p))
      return respond(config, 204)
    }
    const visible = projects.filter((p) => p.status !== 'archived')
    return respond(config, 200, { items: visible, page: 1, size: 20, totalCount: visible.length })
  }
  return requests
}

const listRequests = (requests: Recorded[]) =>
  requests.filter((r) => r.method === 'get' && r.url === '/projects')

function grant(...codes: string[]) {
  useAuthStore.getState().login(fakeJwt())
  useAuthStore.getState().setPermissions(
    useAuthStore.getState().generation,
    'user-1',
    codes.map((code) => ({ code, scopeType: 'global' })),
  )
}

let lastQueryClient: QueryClient

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  lastQueryClient = queryClient
  render(
    <QueryClientProvider client={queryClient}>
      <AntdApp>
        <MemoryRouter>
          <ProjectListPage />
        </MemoryRouter>
      </AntdApp>
    </QueryClientProvider>,
  )
}

describe('ProjectListPage', () => {
  beforeEach(() => grant('project.read', 'project.create', 'project.update', 'user.read'))
  afterEach(() => {
    httpClient.defaults.adapter = originalAdapter
  })

  it('menampilkan proyek dari server dengan status, prioritas, periode, dan departemen', async () => {
    const requests = backend()
    renderPage()

    expect(await screen.findByText('Portal Karyawan', {}, slow)).toBeInTheDocument()
    expect(screen.getByText('Aktif')).toBeInTheDocument()
    expect(screen.getByText('Sedang')).toBeInTheDocument()
    expect(screen.getByText('2026-12-01 s.d. 2026-12-31')).toBeInTheDocument()
    expect(await screen.findByText('Keuangan', {}, slow)).toBeInTheDocument()
    expect(listRequests(requests)[0]?.params).toMatchObject({ page: 1, size: 20 })
  })

  it('membuat proyek baru langsung muncul di daftar tanpa reload', async () => {
    const requests = backend()
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('Portal Karyawan', {}, slow)

    await user.click(screen.getByRole('button', { name: /Proyek baru/ }))
    const drawer = await screen.findByRole('dialog', {}, slow)
    await user.type(within(drawer).getByLabelText(/Kode/), 'crm-01')
    await user.type(within(drawer).getByLabelText(/^Nama/), 'Sistem CRM')
    await user.click(within(drawer).getByRole('button', { name: 'Buat proyek' }))

    expect(await screen.findByText('Sistem CRM', {}, slow)).toBeInTheDocument()
    expect(requests.find((r) => r.method === 'post')?.data).toEqual({
      code: 'crm-01',
      name: 'Sistem CRM',
      description: null,
      priority: 'medium',
      startDate: null,
      endDate: null,
      departmentId: null,
    })
    expect(listRequests(requests).length).toBeGreaterThanOrEqual(2)
  })

  it('tanggal selesai sebelum tanggal mulai ditolak tanpa memanggil server', async () => {
    const requests = backend()
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('Portal Karyawan', {}, slow)

    await user.click(screen.getByRole('button', { name: /Proyek baru/ }))
    const drawer = await screen.findByRole('dialog', {}, slow)
    await user.type(within(drawer).getByLabelText(/Kode/), 'X1')
    await user.type(within(drawer).getByLabelText(/^Nama/), 'X')
    await user.type(within(drawer).getByLabelText('Tanggal mulai'), '2026-12-31')
    await user.type(within(drawer).getByLabelText('Tanggal selesai'), '2026-12-01')
    await user.click(within(drawer).getByRole('button', { name: 'Buat proyek' }))

    expect(
      await within(drawer).findByText('Tanggal selesai tidak boleh sebelum tanggal mulai'),
    ).toBeInTheDocument()
    expect(requests.some((r) => r.method === 'post')).toBe(false)
  })

  it('error validasi dan aturan bisnis dari server tampil di form', async () => {
    backend({
      createStatus: 422,
      createBody: { detail: 'Kode proyek sudah dipakai di tenant ini.' },
    })
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('Portal Karyawan', {}, slow)

    await user.click(screen.getByRole('button', { name: /Proyek baru/ }))
    const drawer = await screen.findByRole('dialog', {}, slow)
    await user.type(within(drawer).getByLabelText(/Kode/), 'WEB')
    await user.type(within(drawer).getByLabelText(/^Nama/), 'Ganda')
    await user.click(within(drawer).getByRole('button', { name: 'Buat proyek' }))

    expect(await within(drawer).findByRole('alert', {}, slow)).toHaveTextContent(
      'Kode proyek sudah dipakai di tenant ini.',
    )
  })

  it('konflik 409 menampilkan pesan dan memuat ulang data terbaru ke form', async () => {
    const requests = backend({ conflictOnUpdate: true })
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('Portal Karyawan', {}, slow)

    await user.click(screen.getByRole('button', { name: 'Ubah' }))
    const drawer = await screen.findByRole('dialog', {}, slow)
    const name = within(drawer).getByLabelText(/^Nama/)
    await user.clear(name)
    await user.type(name, 'Nama Saya')
    await user.click(within(drawer).getByRole('button', { name: 'Simpan' }))

    expect(
      await within(drawer).findByText(/Data telah diubah orang lain/, {}, slow),
    ).toBeInTheDocument()
    expect(requests.find((r) => r.method === 'put')?.ifMatch).toBe('"3"')
    // Daftar dimuat ulang dan form memakai versi terbaru (nama dari orang lain, If-Match baru).
    await waitFor(
      () => expect(within(drawer).getByLabelText(/^Nama/)).toHaveValue('Diubah Orang Lain'),
      slow,
    )
    expect(
      await screen.findByText('Diubah Orang Lain', { selector: 'a' }, slow),
    ).toBeInTheDocument()
  })

  it('pemuatan ulang daftar di latar belakang tidak mengganti isian dan tidak menimpa perubahan orang lain', async () => {
    const requests = backend()
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('Portal Karyawan', {}, slow)

    await user.click(screen.getByRole('button', { name: 'Ubah' }))
    const drawer = await screen.findByRole('dialog', {}, slow)
    const name = within(drawer).getByLabelText(/^Nama/)
    await user.clear(name)
    await user.type(name, 'Nama Saya')

    // Orang lain mengubah proyek, lalu daftar dimuat ulang di latar belakang (mis. koneksi pulih).
    changeBehind({ name: 'Diubah Orang Lain' })
    await lastQueryClient.invalidateQueries()
    expect(
      await screen.findByText('Diubah Orang Lain', { selector: 'a' }, slow),
    ).toBeInTheDocument()
    expect(within(drawer).getByLabelText(/^Nama/)).toHaveValue('Nama Saya')

    // Simpan memakai versi yang dilihat pengguna, sehingga server menolak (409), bukan menimpa diam-diam.
    await user.click(within(drawer).getByRole('button', { name: 'Simpan' }))
    expect(
      await within(drawer).findByText(/Data telah diubah orang lain/, {}, slow),
    ).toBeInTheDocument()
    expect(requests.find((r) => r.method === 'put')?.ifMatch).toBe('"3"')
  })

  it('mengarsipkan proyek meminta konfirmasi lalu mengirim DELETE dengan If-Match', async () => {
    const requests = backend()
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('Portal Karyawan', {}, slow)

    await user.click(screen.getByRole('button', { name: 'Arsipkan' }))
    const confirm = await screen.findByRole('tooltip', {}, slow)
    await user.click(within(confirm).getByRole('button', { name: 'Arsipkan' }))

    await waitFor(() => expect(requests.some((r) => r.method === 'delete')).toBe(true), slow)
    expect(requests.find((r) => r.method === 'delete')).toMatchObject({
      url: '/projects/p-1',
      ifMatch: '"3"',
    })
    await waitFor(() => expect(screen.queryByText('Portal Karyawan')).not.toBeInTheDocument(), slow)
  })

  it('filter status, milik saya, dan pencarian dikirim ke server', async () => {
    const requests = backend()
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('Portal Karyawan', {}, slow)

    await user.click(screen.getByLabelText('Hanya milik saya'))
    await waitFor(() => expect(listRequests(requests).at(-1)?.params?.ownerId).toBe('user-1'))

    await user.type(screen.getByLabelText('Cari proyek'), 'web')
    await waitFor(
      () => expect(listRequests(requests).at(-1)?.params).toMatchObject({ search: 'web', page: 1 }),
      slow,
    )
  })

  it('tanpa permission tulis, tombol buat, ubah, dan arsip tersembunyi', async () => {
    grant('project.read')
    const requests = backend()
    renderPage()

    expect(await screen.findByText('Portal Karyawan', {}, slow)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Proyek baru/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ubah' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Arsipkan' })).not.toBeInTheDocument()
    // Tanpa user.read, daftar departemen tidak diminta.
    expect(requests.some((r) => r.url === '/departments')).toBe(false)
  })
})
