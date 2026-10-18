import { beforeEach, describe, expect, it } from 'vitest'
import { decodeSessionUser, useAuthStore } from '@/features/auth/store/authStore'
import { fakeJwt } from '@/test/tokens'

describe('authStore', () => {
  beforeEach(() =>
    useAuthStore.setState((s) => ({
      accessToken: null,
      user: null,
      status: 'unknown',
      // Generasi selalu naik (seperti di aplikasi) agar refresh yang tertinggal dari tes sebelumnya diabaikan.
      generation: s.generation + 1,
    })),
  )

  it('awalnya status unknown sampai sesi dipulihkan', () => {
    expect(useAuthStore.getState().status).toBe('unknown')
    expect(useAuthStore.getState().accessToken).toBeNull()
  })

  it('setSession membaca user dari klaim sub, tid, dan roles', () => {
    useAuthStore.getState().login(fakeJwt({ sub: 'u-9', tid: '3', roles: ['pm', 'member'] }))

    const state = useAuthStore.getState()
    expect(state.status).toBe('authenticated')
    expect(state.user).toEqual({ id: 'u-9', tenantId: '3', roles: ['pm', 'member'] })
    expect(state.hasRole('pm')).toBe(true)
    expect(state.hasRole('admin')).toBe(false)
  })

  it('token rusak tidak membuat sesi', () => {
    useAuthStore.getState().login('bukan.jwt')

    expect(useAuthStore.getState().status).toBe('anonymous')
    expect(useAuthStore.getState().accessToken).toBeNull()
  })

  it('clear mengakhiri sesi', () => {
    useAuthStore.getState().login(fakeJwt())
    useAuthStore.getState().clear()

    expect(useAuthStore.getState().status).toBe('anonymous')
    expect(useAuthStore.getState().user).toBeNull()
  })

  it('token tidak disimpan di localStorage maupun sessionStorage', () => {
    const token = fakeJwt()
    useAuthStore.getState().login(token)

    expect(JSON.stringify({ ...localStorage })).not.toContain(token)
    expect(JSON.stringify({ ...sessionStorage })).not.toContain(token)
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })

  it('login dan clear menaikkan generasi, refresh (setAccessToken) tidak', () => {
    const start = useAuthStore.getState().generation
    useAuthStore.getState().login(fakeJwt())
    useAuthStore.getState().setAccessToken(fakeJwt({ jti: 'baru' }))
    expect(useAuthStore.getState().generation).toBe(start + 1)

    useAuthStore.getState().clear()
    expect(useAuthStore.getState().generation).toBe(start + 2)
  })

  it('decodeSessionUser menerima roles tunggal berupa string dan menolak klaim yang hilang', () => {
    expect(decodeSessionUser(fakeJwt({ roles: 'admin' }))?.roles).toEqual(['admin'])
    expect(decodeSessionUser(fakeJwt({ tid: undefined }))).toBeNull()
  })
})
