import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import { useAuthStore } from '@/features/auth/store/authStore'
import { fakeJwt } from '@/test/tokens'

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(<RouterProvider router={router} />)
}

describe('MainLayout', () => {
  beforeEach(() => useAuthStore.getState().setSession(fakeJwt({ tid: '7' })))

  it('menampilkan sidebar, tenant, dan halaman Dashboard kosong di /', async () => {
    renderAt('/')

    expect(screen.getByLabelText('Sidebar')).toBeInTheDocument()
    expect(screen.getByText('Tenant 7')).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getByText('Belum ada data')).toBeInTheDocument()
  })

  it('menampilkan halaman 404 untuk path yang tidak dikenal', async () => {
    renderAt('/tidak-ada')

    expect(await screen.findByText('Halaman tidak ditemukan.')).toBeInTheDocument()
  })

  it('menampilkan halaman 403', async () => {
    renderAt('/403')

    expect(await screen.findByText('Anda tidak memiliki akses ke halaman ini.')).toBeInTheDocument()
  })
})
