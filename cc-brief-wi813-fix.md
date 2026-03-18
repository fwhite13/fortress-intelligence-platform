# CC Fix Brief: WI813 — Fix vite.config.ts output path

The build currently produces `dist/src/taskpane/index.html` because the input is
`src/taskpane/index.html` and Vite mirrors the relative path structure.

The spec requires `dist/taskpane/index.html` so that after deployment:
  cp -r dist/* wwwroot/excel-addin/
the URL becomes https://fait.dev.fortressam.ai/excel-addin/taskpane/index.html

The manifest.xml already has `.../taskpane/index.html` (correct for the target).
The build needs to match.

## Fix Required: vite.config.ts — change root to 'src'

Replace the entire vite.config.ts with:

```typescript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  root: 'src', // sets project root to src/ so taskpane/index.html → dist/taskpane/index.html

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
    outDir: '../dist', // relative to root (src/), so output goes to project root dist/
    target: 'es2017', // raised from es2015; WebView2/WKWebView/Edge all support es2017
    rollupOptions: {
      input: {
        taskpane: 'taskpane/index.html', // relative to root (src/taskpane/index.html)
        commands: '../public/commands.html', // commands.html is in public/ at project root
      },
      // No output.format override — defaults to ES modules, which is correct
    },
  },

  base: '/excel-addin/', // must match deployment URL prefix and manifest URLs
});
```

Setting `root: 'src'` means:
- Vite treats `src/` as the project root for HTML resolution
- `taskpane/index.html` (relative to src/) becomes the entry point
- Output path: `dist/taskpane/index.html` ✓ (matches manifest URL)
- `outDir: '../dist'` because it's relative to the new root (src/)

After making this change, confirm the file was written correctly.
Do NOT run npm install or npm run build.
