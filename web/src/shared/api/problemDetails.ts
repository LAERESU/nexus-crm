import { AxiosError } from 'axios'
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'

/** Error dari backend dalam bentuk ProblemDetails (CONTEXT.md bagian 4), dengan `errors` per field untuk 400. */
export interface Problem {
  /** `undefined` bila server tidak terjangkau. */
  status?: number
  detail?: string
  /** Kunci sudah camelCase (`FullName` dari server menjadi `fullName`). */
  fieldErrors: Record<string, string[]>
}

const camelCase = (name: string) => name.charAt(0).toLowerCase() + name.slice(1)

export function parseProblem(error: unknown): Problem {
  if (!(error instanceof AxiosError) || !error.response) return { fieldErrors: {} }

  const body: unknown = error.response.data
  const fieldErrors: Record<string, string[]> = {}
  let detail: string | undefined
  if (typeof body === 'object' && body !== null) {
    const { detail: rawDetail, errors } = body as { detail?: unknown; errors?: unknown }
    if (typeof rawDetail === 'string') detail = rawDetail
    if (typeof errors === 'object' && errors !== null) {
      for (const [key, value] of Object.entries(errors)) {
        const messages = (Array.isArray(value) ? value : [value]).filter(
          (m): m is string => typeof m === 'string',
        )
        if (messages.length > 0) fieldErrors[camelCase(key)] = messages
      }
    }
  }
  return { status: error.response.status, detail, fieldErrors }
}

/** Pesan umum untuk pengguna bila error tidak terkait field tertentu. */
export function problemMessage(problem: Problem): string {
  switch (problem.status) {
    case undefined:
      return 'Tidak dapat terhubung ke server. Coba lagi.'
    case 403:
      return 'Anda tidak memiliki izin untuk tindakan ini.'
    case 409:
      return 'Data sudah diubah pengguna lain. Muat ulang lalu coba lagi.'
    case 404:
    case 422:
      return problem.detail ?? 'Permintaan tidak dapat diproses.'
    case 400:
      return 'Periksa kembali data yang diisi.'
    default:
      return 'Terjadi kesalahan pada server. Coba lagi.'
  }
}

/**
 * Memasang error validasi server ke field form yang sesuai. Mengembalikan pesan tingkat form untuk error yang
 * tidak punya field di form ini, atau `null` bila semuanya sudah tampil di field.
 */
export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: readonly Path<T>[],
): string | null {
  const problem = parseProblem(error)
  const known = new Set<string>(fields)
  const rest: string[] = []
  let applied = false

  for (const [key, messages] of Object.entries(problem.fieldErrors)) {
    if (known.has(key)) {
      setError(key as Path<T>, { type: 'server', message: messages.join(' ') })
      applied = true
    } else {
      rest.push(...messages)
    }
  }

  if (rest.length > 0) return rest.join(' ')
  return applied ? null : problemMessage(problem)
}
