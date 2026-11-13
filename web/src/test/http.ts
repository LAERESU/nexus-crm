import { AxiosError } from 'axios'
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'

/** Jawaban backend tiruan untuk adapter axios di tes: status 4xx/5xx menjadi AxiosError seperti di aplikasi. */
export function respond(
  config: InternalAxiosRequestConfig,
  status: number,
  data: unknown = {},
  headers: Record<string, string> = {},
) {
  const response: AxiosResponse = { data, status, statusText: String(status), headers, config }
  return status >= 400
    ? Promise.reject(new AxiosError('gagal', String(status), config, undefined, response))
    : Promise.resolve(response)
}

/** Halaman dimuat lazy; import pertama Ant Design di jsdom bisa lebih dari 1 detik. */
export const slow = { timeout: 5000 }
