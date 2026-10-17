import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '@/features/auth/store/authStore'
import { httpClient } from '@/shared/api/httpClient'

async function captureHeaders() {
  let captured: InternalAxiosRequestConfig | undefined
  await httpClient.get('/ping', {
    adapter: async (config) => {
      captured = config
      return { data: {}, status: 200, statusText: 'OK', headers: {}, config }
    },
  })
  return captured!.headers
}

describe('httpClient', () => {
  beforeEach(() => useAuthStore.getState().clear())

  it('memakai base URL /api/v1 dan mengirim cookie', () => {
    expect(httpClient.defaults.baseURL).toBe('/api/v1')
    expect(httpClient.defaults.withCredentials).toBe(true)
  })

  it('menambahkan X-Correlation-Id pada setiap request', async () => {
    const headers = await captureHeaders()

    expect(headers.get('X-Correlation-Id')).toMatch(/^[0-9a-f-]{36}$/)
  })

  it('menambahkan Authorization bila sudah login', async () => {
    useAuthStore.getState().setSession('abc', { id: '1', name: 'A', tenantId: 't' }, [])

    const headers = await captureHeaders()

    expect(headers.get('Authorization')).toBe('Bearer abc')
  })

  it('tidak menambahkan Authorization bila belum login', async () => {
    const headers = await captureHeaders()

    expect(headers.get('Authorization')).toBeFalsy()
  })
})
