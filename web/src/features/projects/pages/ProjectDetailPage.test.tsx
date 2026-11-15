import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { App as AntdApp } from 'antd'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '@/features/auth/store/authStore'
import type { Milestone, Project, ProjectMember } from '@/features/projects/api/projectsApi'
import ProjectDetailPage from '@/features/projects/pages/ProjectDetailPage'
import { httpClient } from '@/shared/api/httpClient'
import { respond, slow } from '@/test/http'
import { fakeJwt } from '@/test/tokens'

const originalAdapter = httpClient.defaults.adapter

const project: Project = {
  id: 'p-1',
  code: 'WEB',
  name: 'Portal Karyawan',
  description: 'Portal internal',
  status: 'active',
  priority: 'high',
  startDate: '2026-12-01',
  endDate: '2026-12-31',
  progressPct: 40,
  ownerId: 'u-manager',
  departmentId: null,
  rowVersion: 2,
  createdAt: '2026-11-01T00:00:00Z',
}

const member = (userId: string, fullName: string, role: ProjectMember['role']): ProjectMember => ({
  userId,
  fullName,
  email: `${userId}@contoh.com`,
  role,
  joinedAt: '2026-11-02T00:00:00Z',
  isActive: true,
})

interface Recorded {
  method?: string
  url?: string
  data?: unknown
}

/** Backend tiruan yang menyimpan anggota dan milestone, seperti server sungguhan. */
function backend({
  detailStatus = 200,
  milestoneStatus = 201,
  milestonesListStatus = 200,
  extraMembers = 0,
} = {}) {
  let members = [
    member('u-manager', 'Maya Manager', 'manager'),
    member('u-staf', 'Sari Staf', 'member'),
    ...Array.from({ length: extraMembers }, (_, i) => member(`u-${i}`, `Anggota ${i}`, 'viewer')),
  ]
  let milestones: Milestone[] = [
    {
      id: 'm-1',
      projectId: 'p-1',
      name: 'Rilis Beta',
      description: null,
      dueDate: '2026-12-10',
      status: 'open',
      rowVersion: 0,
      createdAt: '2026-11-03T00:00:00Z',
    },
  ]
  const requests: Recorded[] = []
  httpClient.defaults.adapter = async (config) => {
    const data = typeof config.data === 'string' ? JSON.parse(config.data) : undefined
    requests.push({ method: config.method, url: config.url, data })
    const url = config.url ?? ''
    if (url === '/users')
      return respond(config, 200, {
        items: [
          { id: 'u-baru', fullName: 'Budi Baru', email: 'budi@contoh.com', status: 'active' },
          { id: 'u-staf', fullName: 'Sari Staf', email: 'u-staf@contoh.com', status: 'active' },
        ],
        page: 1,
        size: 20,
        totalCount: 2,
      })
    if (url === '/projects/p-1')
      return detailStatus === 200 ? respond(config, 200, project) : respond(config, detailStatus)
    if (url === '/projects/p-1/members' && config.method === 'post') {
      const added = member(data.userId, 'Budi Baru', data.role)
      members = [...members, added]
      return respond(config, 201, added)
    }
    if (url.startsWith('/projects/p-1/members/') && config.method === 'delete') {
      members = members.filter((m) => m.userId !== url.split('/').pop())
      return respond(config, 204)
    }
    if (url === '/projects/p-1/members') {
      // Paging seperti server: maksimal 100 per halaman.
      const { page = 1, size = 100 } = (config.params ?? {}) as { page?: number; size?: number }
      return respond(config, 200, {
        items: members.slice((page - 1) * size, page * size),
        page,
        size,
        totalCount: members.length,
      })
    }
    if (url === '/projects/p-1/milestones' && config.method === 'post') {
      if (milestoneStatus !== 201)
        return respond(config, milestoneStatus, {
          detail:
            'Tanggal milestone harus di dalam rentang tanggal proyek (2026-12-01 s.d. 2026-12-31).',
        })
      const created: Milestone = {
        ...milestones[0]!,
        id: 'm-2',
        ...data,
        status: 'open',
        rowVersion: 0,
      }
      milestones = [...milestones, created]
      return respond(config, 201, created)
    }
    if (url === '/projects/p-1/milestones' && milestonesListStatus !== 200)
      return respond(config, milestonesListStatus)
    if (url === '/projects/p-1/milestones')
      return respond(config, 200, {
        items: milestones,
        page: 1,
        size: 100,
        totalCount: milestones.length,
      })
    if (url.startsWith('/projects/p-1/milestones/') && config.method === 'put') {
      milestones = milestones.map((m) =>
        m.id === 'm-1' ? { ...m, status: data.status, rowVersion: 1 } : m,
      )
      return respond(config, 200, milestones[0])
    }
    return respond(config, 404)
  }
  return requests
}

function signIn(userId: string, ...codes: string[]) {
  useAuthStore.getState().login(fakeJwt({ sub: userId }))
  useAuthStore.getState().setPermissions(
    useAuthStore.getState().generation,
    userId,
    codes.map((code) => ({ code, scopeType: 'global' })),
  )
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      { path: '/projects/:id', element: <ProjectDetailPage /> },
      { path: '/projects', element: <p>Daftar proyek</p> },
    ],
    { initialEntries: ['/projects/p-1'] },
  )
  render(
    <QueryClientProvider client={queryClient}>
      <AntdApp>
        <RouterProvider router={router} />
      </AntdApp>
    </QueryClientProvider>,
  )
}

const managerPermissions = ['user.read', 'project.read', 'project.update', 'project.member.manage']

describe('ProjectDetailPage', () => {
  afterEach(() => {
    httpClient.defaults.adapter = originalAdapter
  })

  it('ringkasan menampilkan data proyek dan nama pemilik dari daftar anggota', async () => {
    signIn('u-staf', 'project.read')
    backend()
    renderPage()

    expect(
      await screen.findByRole('heading', { name: 'Portal Karyawan' }, slow),
    ).toBeInTheDocument()
    expect(screen.getByText('Tinggi')).toBeInTheDocument()
    expect(screen.getByText('2026-12-01 s.d. 2026-12-31')).toBeInTheDocument()
    expect(await screen.findByText('Maya Manager', {}, slow)).toBeInTheDocument()
    expect(screen.getByText('Portal internal')).toBeInTheDocument()
  })

  it('manager bisa menambah dan mengeluarkan anggota dari UI', async () => {
    signIn('u-manager', ...managerPermissions)
    const requests = backend()
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: /Anggota/ }, slow))
    await user.type(await screen.findByRole('combobox', { name: 'Pilih user' }, slow), 'budi')
    await user.click(await screen.findByText('Budi Baru (budi@contoh.com)', {}, slow))
    await user.click(screen.getByRole('button', { name: 'Tambah anggota' }))

    await waitFor(
      () =>
        expect(requests.find((r) => r.method === 'post')?.data).toEqual({
          userId: 'u-baru',
          role: 'member',
        }),
      slow,
    )
    expect(await screen.findByText('Budi Baru', { selector: 'td' }, slow)).toBeInTheDocument()

    const row = screen.getByText('Sari Staf', { selector: 'td' }).closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Keluarkan' }))
    await user.click(
      within(await screen.findByRole('tooltip', {}, slow)).getByRole('button', {
        name: 'Keluarkan',
      }),
    )

    await waitFor(
      () =>
        expect(
          requests.some((r) => r.method === 'delete' && r.url === '/projects/p-1/members/u-staf'),
        ).toBe(true),
      slow,
    )
    await waitFor(
      () => expect(screen.queryByText('Sari Staf', { selector: 'td' })).not.toBeInTheDocument(),
      slow,
    )
  })

  it('anggota biasa hanya melihat anggota, tanpa tombol tambah, ubah peran, atau keluarkan', async () => {
    // Memegang project.member.manage secara umum, tetapi berperan member di proyek ini.
    signIn('u-staf', ...managerPermissions)
    backend()
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: /Anggota/ }, slow))
    expect(await screen.findByText('Maya Manager', { selector: 'td' }, slow)).toBeInTheDocument()
    expect(screen.getByText('Hanya manager proyek yang bisa mengubah anggota.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Tambah anggota' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Keluarkan' })).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: /Peran/ })).not.toBeInTheDocument()
  })

  it('manager membuat milestone; tanggal di luar rentang proyek menampilkan pesan server', async () => {
    signIn('u-manager', ...managerPermissions)
    backend({ milestoneStatus: 422 })
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: 'Milestone' }, slow))
    expect(await screen.findByText('Rilis Beta', {}, slow)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Milestone baru' }))
    const dialog = await screen.findByRole('dialog', {}, slow)
    await user.type(within(dialog).getByLabelText(/^Nama/), 'Rilis Final')
    await user.type(within(dialog).getByLabelText(/Tanggal jatuh tempo/), '2027-01-15')
    await user.click(within(dialog).getByRole('button', { name: 'Buat milestone' }))

    expect(await within(dialog).findByRole('alert', {}, slow)).toHaveTextContent(
      'di dalam rentang tanggal proyek',
    )
  })

  it('manager menandai milestone selesai', async () => {
    signIn('u-manager', ...managerPermissions)
    const requests = backend()
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: 'Milestone' }, slow))
    await user.click(await screen.findByRole('button', { name: 'Tandai selesai' }, slow))

    await waitFor(
      () =>
        expect(requests.find((r) => r.method === 'put')?.data).toMatchObject({
          status: 'completed',
          dueDate: '2026-12-10',
        }),
      slow,
    )
    expect(await screen.findByRole('button', { name: 'Buka lagi' }, slow)).toBeInTheDocument()
  })

  it('milestone yang gagal dimuat tampil sebagai error, bukan daftar kosong', async () => {
    signIn('u-manager', ...managerPermissions)
    backend({ milestonesListStatus: 500 })
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: 'Milestone' }, slow))

    expect(await screen.findByRole('button', { name: 'Coba lagi' }, slow)).toBeInTheDocument()
    expect(screen.queryByText('Belum ada milestone')).not.toBeInTheDocument()
  })

  it('anggota lebih dari 100 dimuat semua halaman', async () => {
    signIn('u-149', ...managerPermissions)
    const requests = backend({ extraMembers: 160 })
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: /Anggota \(162\)/ }, slow))
    expect(
      requests.filter((r) => r.url === '/projects/p-1/members' && r.method === 'get'),
    ).toHaveLength(2)
    // u-149 (di halaman kedua) berperan viewer: dikenali sebagai anggota, tetapi bukan manager.
    expect(screen.getByText('Hanya manager proyek yang bisa mengubah anggota.')).toBeInTheDocument()
  })

  it('bukan anggota melihat pesan 403 dan tautan kembali ke daftar', async () => {
    signIn('u-lain', 'project.read')
    const requests = backend({ detailStatus: 403 })
    renderPage()

    expect(await screen.findByText('Anda bukan anggota proyek ini', {}, slow)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Kembali ke daftar proyek' })).toBeInTheDocument()
    expect(requests.some((r) => r.url === '/projects/p-1/members')).toBe(false)
  })
})
