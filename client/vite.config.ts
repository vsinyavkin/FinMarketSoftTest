import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Билд кладём в wwwroot бэкенда; в dev проксируем /api и /hubs на Kestrel.
// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/TickTraderProxy.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5080', changeOrigin: true, ws: true },
    },
  },
})
