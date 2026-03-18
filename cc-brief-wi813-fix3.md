# CC Fix Brief: WI813 — Final vite.config.ts fix + manifest URL correction

The `root: 'src'` approach causes issues with cross-root paths for commands.html.
The simplest correct approach: keep project root, use `src/taskpane/index.html` as input,
and update the manifest to reflect the ACTUAL output path.

When Vite input is `src/taskpane/index.html` (relative to project root),
the output is `dist/src/taskpane/index.html`.
After `cp -r dist/* wwwroot/excel-addin/`, the URL becomes:
  https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html

So manifest.xml and manifest.local.xml must be updated to use the correct paths.

## CHANGE 1: vite.config.ts — remove root: 'src', fix outDir and input paths

Replace the entire vite.config.ts with:

```typescript
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
        taskpane: 'src/taskpane/index.html', // HTML entry point — outputs to dist/src/taskpane/index.html
        commands: 'public/commands.html',    // ribbon commands page — outputs to dist/commands.html
      },
      // No output.format override — defaults to ES modules, which is correct
    },
  },

  base: '/excel-addin/', // must match deployment URL prefix and manifest URLs
});
```

## CHANGE 2: src/taskpane/index.html — restore original absolute script path

The script src should be `/src/taskpane/index.tsx` (absolute path works with default root):

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>FAIT for Excel</title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet" />
    <!-- Office JS — required for all Office Add-ins -->
    <script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js" type="text/javascript"></script>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/taskpane/index.tsx"></script>
  </body>
</html>
```

## CHANGE 3: manifest.xml — update URLs to match actual build output path

The actual build output is dist/src/taskpane/index.html.
After cp -r dist/* wwwroot/excel-addin/, the URL is:
  https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html

Update manifest.xml:

1. In `<DefaultSettings>`, change to:
   ```xml
   <SourceLocation DefaultValue="https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html"/>
   ```

2. In `<Resources><bt:Urls>`, change to:
   ```xml
   <bt:Url id="Taskpane.Url" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html"/>
   ```

## CHANGE 4: manifest.local.xml — update dev URLs to use relative path

In manifest.local.xml, the localhost URLs should be:
- `<SourceLocation DefaultValue="https://localhost:3000/src/taskpane/index.html"/>`
- `<bt:Url id="Taskpane.Url" DefaultValue="https://localhost:3000/src/taskpane/index.html"/>`

Check the current manifest.local.xml — it likely already has these localhost:3000 URLs.
If they already say `/src/taskpane/index.html`, no changes needed.
If they say something else, update them to `https://localhost:3000/src/taskpane/index.html`.

After making these changes, confirm all 4 files were updated correctly.
Do NOT run npm run build.
