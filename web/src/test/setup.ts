import '@testing-library/jest-dom/vitest'

// jsdom tidak menyediakan matchMedia, sedangkan komponen antd (Sider, Grid) memakainya.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
})

// jsdom juga tidak menyediakan ResizeObserver, yang dipakai Table antd.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
}
