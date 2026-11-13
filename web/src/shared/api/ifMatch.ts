/** Konfigurasi request dengan header `If-Match` berisi `rowVersion` (CONTEXT.md bagian 6). */
export const ifMatch = (rowVersion: number) => ({ headers: { 'If-Match': `"${rowVersion}"` } })
