import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosAdapter, AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '@/features/auth/store/authStore'
import { httpClient } from '@/shared/api/httpClient'
import { fakeJwt } from '@/test/tokens'

const originalAdapter = httpClient.defaults.adapter
const oldToken = fakeJwt({ jti: 'lama' })
const newToken = fakeJwt({ jti: 'baru' })

function respond(
  config: InternalAxiosRequestConfig,
  status: number,
  data: unknown = {},
): Promise<AxiosResponse> {
  const response: AxiosResponse = { data, status, statusText: String(status), headers: {}, config }
  return status >= 400
    ? Promise.reject(new AxiosError('gagal', String(status), config, undefined, response))
    : Promise.resolve(response)
}

/** Server tiruan: /auth/refresh memberi token baru (atau gagal), endpoint lain menerima hanya token baru. */
function fakeServer(options: { refreshFails?: boolean } = {}) {
  const calls: { url?: string; authorization?: string }[] = []
  const adapter: AxiosAdapter = async (config) => {
    const authorization = AxiosHeaders.from(config.headers).get('Authorization')?.toString()
    calls.push({ url: config.url, authorization })
    if (config.url === '/auth/refresh') {
      await new Promise((r) => setTimeout(r, 10)) // beri waktu request lain ikut menunggu
      return options.refreshFails
        ? respond(config, 401)
        : respond(config, 200, { accessToken: newToken })
    }
    if (config.url === '/auth/login') return respond(config, 401)
    return authorization === `Bearer ${newToken}`
      ? respond(config, 200, { ok: true })
      : respond(config, 401)
  }
  httpClient.defaults.adapter = adapter
  return calls
}

describe('httpClient', () => {
  beforeEach(() => useAuthStore.getState().setSession(oldToken))
  afterEach(() => {
    httpClient.defaults.adapter = originalAdapter
    useAuthStore.getState().clear()
  })

  it('memakai base URL /api/v1, mengirim cookie, dan menyisipkan X-Correlation-Id', async () => {
    const calls: Record<string, string | undefined>[] = []
    httpClient.defaults.adapter = async (config) => {
      calls.push({
        correlation: AxiosHeaders.from(config.headers).get('X-Correlation-Id')?.toString(),
      })
      return respond(config, 200)
    }

    await httpClient.get('/ping')

    expect(httpClient.defaults.baseURL).toBe('/api/v1')
    expect(httpClient.defaults.withCredentials).toBe(true)
    expect(calls[0]?.correlation).toMatch(/^[0-9a-f-]{36}$/)
  })

  it('401 memicu satu refresh lalu request diulang dengan token baru', async () => {
    const calls = fakeServer()

    const response = await httpClient.get('/projects')

    expect(response.status).toBe(200)
    expect(calls.map((c) => c.url)).toEqual(['/projects', '/auth/refresh', '/projects'])
    expect(calls[2]?.authorization).toBe(`Bearer ${newToken}`)
    expect(useAuthStore.getState().accessToken).toBe(newToken)
  })

  it('banyak request 401 bersamaan berbagi satu refresh (rotasi refresh token NX-013)', async () => {
    const calls = fakeServer()

    const responses = await Promise.all([
      httpClient.get('/a'),
      httpClient.get('/b'),
      httpClient.get('/c'),
    ])

    expect(responses.every((r) => r.status === 200)).toBe(true)
    expect(calls.filter((c) => c.url === '/auth/refresh')).toHaveLength(1)
  })

  it('refresh gagal mengakhiri sesi dan request tidak diulang', async () => {
    const calls = fakeServer({ refreshFails: true })

    await expect(httpClient.get('/projects')).rejects.toBeInstanceOf(AxiosError)

    expect(useAuthStore.getState().status).toBe('anonymous')
    expect(calls.map((c) => c.url)).toEqual(['/projects', '/auth/refresh'])
  })

  it('401 dari endpoint auth tidak memicu refresh', async () => {
    const calls = fakeServer()

    await expect(
      httpClient.post('/auth/login', {}, { skipAuthRefresh: true }),
    ).rejects.toBeInstanceOf(AxiosError)

    expect(calls.map((c) => c.url)).toEqual(['/auth/login'])
  })

  it('request yang tetap 401 setelah refresh tidak diulang lagi (tanpa loop)', async () => {
    const calls: string[] = []
    httpClient.defaults.adapter = async (config) => {
      calls.push(config.url ?? '')
      return config.url === '/auth/refresh'
        ? respond(config, 200, { accessToken: newToken })
        : respond(config, 401)
    }

    await expect(httpClient.get('/forbidden')).rejects.toBeInstanceOf(AxiosError)

    expect(calls).toEqual(['/forbidden', '/auth/refresh', '/forbidden'])
  })
})
