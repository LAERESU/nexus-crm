import { beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '@/features/auth/store/authStore'

const user = { id: 'u1', name: 'Budi', tenantId: 't1' }

describe('authStore', () => {
  beforeEach(() => useAuthStore.getState().clear())

  it('awalnya belum terautentikasi', () => {
    const state = useAuthStore.getState()
    expect(state.isAuthenticated).toBe(false)
    expect(state.accessToken).toBeNull()
  })

  it('setSession menyimpan token, user, dan permission', () => {
    useAuthStore.getState().setSession('token', user, ['project.read'])

    const state = useAuthStore.getState()
    expect(state.isAuthenticated).toBe(true)
    expect(state.accessToken).toBe('token')
    expect(state.hasPermission('project.read')).toBe(true)
    expect(state.hasPermission('sop.approve')).toBe(false)
  })

  it('clear menghapus sesi', () => {
    useAuthStore.getState().setSession('token', user, ['project.read'])
    useAuthStore.getState().clear()

    expect(useAuthStore.getState().isAuthenticated).toBe(false)
    expect(useAuthStore.getState().permissions).toEqual([])
  })

  it('tidak menyimpan token di localStorage', () => {
    useAuthStore.getState().setSession('rahasia', user, [])

    expect(JSON.stringify({ ...localStorage })).not.toContain('rahasia')
  })
})
