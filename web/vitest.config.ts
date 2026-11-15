import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // Tes UI Ant Design (banyak interaksi di jsdom) bisa melewati 5 detik saat semua file berjalan paralel di mesin
    // yang sedang sibuk; tes yang benar-benar macet tetap gagal, hanya 10 detik lebih lambat.
    testTimeout: 15_000,
  },
})
