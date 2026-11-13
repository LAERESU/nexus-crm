/** Batas `size` endpoint list (CONTEXT.md bagian 6). */
export const MAX_PAGE_SIZE = 100

/** Bentuk respons semua endpoint list (CONTEXT.md bagian 6). */
export interface PagedResult<T> {
  items: T[]
  page: number
  size: number
  totalCount: number
}
