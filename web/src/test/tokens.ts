/** Membuat JWT palsu (tanpa tanda tangan sah) untuk tes; frontend hanya membaca payload untuk tampilan. */
export function fakeJwt(claims: Record<string, unknown> = {}): string {
  const encode = (value: unknown) =>
    btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
  return [
    encode({ alg: 'RS256', typ: 'JWT' }),
    encode({ sub: 'user-1', tid: '7', roles: ['admin'], ...claims }),
    'tanda-tangan',
  ].join('.')
}
