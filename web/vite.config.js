import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'https://localhost:7171', // your API's https port
        changeOrigin: true,
        secure: false, // trusts the local dev certificate only, never in production
      },
    },
  },
})