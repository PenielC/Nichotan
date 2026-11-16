/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In development the API runs on :5210 and is reached through /api on this origin,
// so the refresh-token cookie stays same-origin (as it is in production behind one domain).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': import.meta.dirname + '/src' } },
  server: {
    port: 5175,
    strictPort: true,
    proxy: { '/api': { target: 'http://localhost:5210', changeOrigin: false } },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // jsdom is slow to start on some Windows machines; starting files one at a time avoids worker start-up timeouts.
    fileParallelism: false,
  },
})
