import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// API target defaults to the backend on :6001; override with API_TARGET for local testing.
const apiTarget = process.env.API_TARGET ?? 'http://localhost:6001'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 6173,
    proxy: {
      '/api': apiTarget,
      '/uploads': apiTarget,
    },
  },
  test: {
    environment: 'happy-dom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
})
