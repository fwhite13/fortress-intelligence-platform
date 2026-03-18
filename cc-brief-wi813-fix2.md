# CC Fix Brief: WI813 — Fix HTML entry point path resolution

The previous vite.config.ts fix set `root: 'src'`, but `src/taskpane/index.html` still has
an absolute path `/src/taskpane/index.tsx` in the script tag. When Vite's root is `src/`, that
absolute path resolves to `src/src/taskpane/index.tsx` which doesn't exist.

There are two options. Use Option B (simpler, no root change needed):

## Option B: Keep root at project root, use rollup output.entryFileNames trick

Actually the cleanest solution with Vite 5/6 when root is NOT changed:
The output directory mirrors the INPUT path structure relative to the rollup input.

When input key is `taskpane: 'src/taskpane/index.html'`, Vite outputs to `dist/src/taskpane/index.html`.

To get `dist/taskpane/index.html`, we can instead use the input file path `taskpane/index.html`
if we put it at `src/taskpane/index.html` BUT reference it as `taskpane/index.html` by setting
Vite root to `src` AND fixing the script src in index.html.

## CHANGE 1: Fix src/taskpane/index.html script src

In `src/taskpane/index.html`, the script tag currently has:
```html
<script type="module" src="/src/taskpane/index.tsx"></script>
```

When Vite root is `src/`, the absolute path `/src/taskpane/index.tsx` incorrectly resolves to
`src/src/taskpane/index.tsx`. Change it to a relative path:
```html
<script type="module" src="./index.tsx"></script>
```

A relative `./index.tsx` correctly resolves to `src/taskpane/index.tsx` regardless of root.

## CHANGE 2: Revert vite.config.ts to simpler approach (no root change)

The `root: 'src'` approach has too many edge cases. Instead, keep root at project root
but use a symlink/alias approach — actually the simplest fix is:

Replace vite.config.ts with a version that does NOT use `root: 'src'` and instead
accepts that `src/taskpane/index.html` will output to `dist/src/taskpane/index.html`,
THEN adds a post-build copy step. But that's complex.

## ACTUAL BEST FIX: Keep root: 'src', fix the script src in index.html

Do BOTH of these:

### Step 1: Update src/taskpane/index.html

Change the script tag from:
```html
<script type="module" src="/src/taskpane/index.tsx"></script>
```
to:
```html
<script type="module" src="./index.tsx"></script>
```

This makes the path relative to the HTML file's location, which works correctly
whether Vite root is project root or `src/`.

### Step 2: Verify vite.config.ts still has root: 'src'

The current vite.config.ts should have `root: 'src'`. Confirm it has:
- `root: 'src'`
- `outDir: '../dist'`
- `input: { taskpane: 'taskpane/index.html', commands: '../public/commands.html' }`
- No `output.format` override

If any of these are missing, restore the full config:

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
    target: 'es2017',
    rollupOptions: {
      input: {
        taskpane: 'taskpane/index.html', // relative to root (src/taskpane/index.html)
        commands: '../public/commands.html', // public/ is at project root
      },
      // No output.format override — defaults to ES modules, which is correct
    },
  },

  base: '/excel-addin/', // must match deployment URL prefix and manifest URLs
});
```

After making both changes, confirm:
1. src/taskpane/index.html now has `<script type="module" src="./index.tsx">`
2. vite.config.ts has root: 'src' and outDir: '../dist'

Do NOT run npm run build.
