import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  plugins: [
    react(),
    mkcert(), // generates locally-trusted HTTPS cert for dev
  ],

  server: {
    port: 3000,
    host: 'localhost',
    https: true, // required — Office Add-ins reject http://
  },

  build: {
    outDir: 'dist',
    target: 'es2017', // raised from es2015; WebView2/WKWebView/Edge all support es2017
    rollupOptions: {
      input: {
        taskpane:       'src/taskpane/index.html',        // HTML entry point — outputs to dist/src/taskpane/index.html
        commands:       'public/commands.html',           // ribbon commands page — outputs to dist/commands.html
        'auth-dialog':  'src/taskpane/auth/auth-dialog.html', // auth dialog — outputs to dist/auth-dialog.html
      },
      // No output.format override — defaults to ES modules, which is correct
    },
  },

  base: '/excel-addin/', // must match deployment URL prefix and manifest URLs
});
