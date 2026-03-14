import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    https: true,
    port: 3000,
  },
  build: {
    outDir: 'dist',
    rollupOptions: {
      input: {
        taskpane: 'src/taskpane/index.html',
      },
    },
  },
  base: '/excel-addin/',
});
