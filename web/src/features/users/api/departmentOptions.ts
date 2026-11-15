import type { Department } from '@/features/users/api/usersApi'

/**
 * Pilihan departemen untuk form: departemen yang sedang terpasang tetap bisa ditampilkan walau tidak termasuk daftar
 * yang dimuat (mis. di luar 100 departemen pertama).
 */
export function withCurrentDepartment(
  departments: Department[],
  currentId: string | null | undefined,
  currentName?: string | null,
): Department[] {
  if (!currentId || departments.some((d) => d.id === currentId)) return departments
  return [
    ...departments,
    { id: currentId, code: '', name: currentName ?? 'Departemen saat ini', parentId: null },
  ]
}
