import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  plugins: [
    react(),
    mkcert(),
  ],

  server: {
    port: 3001,
    host: '127.0.0.1',
    https: true,
  },

  build: {
    outDir: 'dist',
    target: 'es2017',
    rollupOptions: {
      input: {
        taskpane: 'src/taskpane/index.html',
        commands: 'public/commands.html',
      },
      output: {
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name][extname]',
      },
    },
  },

  base: '/ppt-addin/',
});
