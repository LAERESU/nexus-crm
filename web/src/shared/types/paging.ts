/** Bentuk respons semua endpoint list (CONTEXT.md bagian 6). */
export interface PagedResult<T> {
  items: T[]
  page: number
  size: number
  totalCount: number
}

export const diSengajaMerusak: any = 1
