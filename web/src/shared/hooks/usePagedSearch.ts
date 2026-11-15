import { useState } from 'react'
import { useDebouncedValue } from '@/shared/hooks/useDebouncedValue'

/**
 * Kata kunci ber-debounce + paging untuk halaman daftar. Halaman berlaku untuk kata kunci saat halaman itu dipilih;
 * kata kunci baru selalu mulai dari halaman 1, tanpa request antara dengan pasangan halaman/kata kunci yang tidak cocok.
 */
export function usePagedSearch(defaultSize = 20) {
  const [searchText, setSearchText] = useState('')
  const [paging, setPaging] = useState({ page: 1, forSearch: '' })
  const [size, setSize] = useState(defaultSize)

  const search = useDebouncedValue(searchText.trim())
  const page = paging.forSearch === search ? paging.page : 1
  const goToPage = (next: number) => setPaging({ page: next, forSearch: search })

  return {
    searchText,
    setSearchText,
    /** Kata kunci setelah debounce; undefined bila kosong. */
    search: search || undefined,
    page,
    size,
    goToPage,
    /** Untuk `pagination.onChange` Table: ganti ukuran halaman kembali ke halaman pertama. */
    changePage: (nextPage: number, nextSize: number) => {
      goToPage(nextSize === size ? nextPage : 1)
      setSize(nextSize)
    },
    /**
     * Dipanggil saat render: bila data menyusut (baris dihapus/diubah orang lain) dan halaman ini kosong, mundur ke
     * halaman terakhir yang ada.
     */
    clampToLastPage: (
      result: { totalCount: number; items: unknown[] } | undefined,
      isPlaceholder: boolean,
    ) => {
      if (!result || isPlaceholder || result.items.length > 0) return
      const lastPage = Math.max(1, Math.ceil(result.totalCount / size))
      if (page > lastPage) goToPage(lastPage)
    },
  }
}
